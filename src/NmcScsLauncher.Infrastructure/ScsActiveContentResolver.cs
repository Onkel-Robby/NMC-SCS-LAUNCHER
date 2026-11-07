using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

/// <summary>Resolves the selected profile's intended mounts without treating DLC presence as entitlement.</summary>
public sealed class ScsActiveContentResolver : IScsActiveContentResolver
{
    private const int MaximumMods = 512;
    private const int MaximumEntries = 4096;
    private const int MaximumLayers = 512;
    private const long MaximumMetadataFileBytes = 8 * 1024 * 1024;
    private const long MaximumExecutableBytes = 256 * 1024 * 1024;
    private static readonly HashSet<string> ManifestAttributes = new(StringComparer.Ordinal)
    {
        "package_version", "display_name", "author", "category", "icon", "description_file",
        "compatible_versions", "dlc_dependencies", "mp_mod_optional"
    };
    // These stock mount roles were observed in both installed games' mount logs.
    // version.sii pack_verifiers are opaque hashes, not a filename/activation list.
    private static readonly HashSet<string> StockArchives = new(StringComparer.OrdinalIgnoreCase)
    {
        "version.scs", "effect.scs", "base.scs", "base_map.scs", "base_vehicle.scs", "base_share.scs",
        "def.scs", "core.scs", "locale.scs", "base_cfg.scs"
    };
    private readonly IScsSaveCodec _codec;
    private readonly IGameInstallationDetector _detector;
    private readonly IWorkshopContentLocator _workshop;
    private readonly Func<string, string?> _installedVersionReader;

    public ScsActiveContentResolver(IScsSaveCodec codec, IGameInstallationDetector detector,
        IWorkshopContentLocator workshop, Func<string, string?>? installedVersionReader = null)
    {
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        _workshop = workshop ?? throw new ArgumentNullException(nameof(workshop));
        _installedVersionReader = installedVersionReader ?? ReadInstalledVersion;
    }

    public Task<ScsActiveContentEvidence> ResolveAsync(ScsEffectiveContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Profile);
        ArgumentNullException.ThrowIfNull(request.Save);
        return Task.Run(() => ResolveCoreAsync(request, cancellationToken), cancellationToken);
    }

    private async Task<ScsActiveContentEvidence> ResolveCoreAsync(ScsEffectiveContentRequest request, CancellationToken token)
    {
        var evidence = new Evidence(token);
        var diagnostics = new List<string>();
        var layers = new List<ScsContentLayer>();
        IReadOnlyList<ScsActiveMod> mods = [];
        var game = request.Profile.GameType;
        var gameVersion = string.Empty;
        var verified = true;
        void Fail(string message) { verified = false; if (diagnostics.Count < 128) diagnostics.Add(message); }
        ScsActiveContentEvidence Result() => new(game, gameVersion, verified, mods, layers.ToArray(), evidence.Files, diagnostics.ToArray());

        try
        {
            ValidateSelection(request);
            var profile = SingleUnit(await ReadSaveMetadataAsync(request.Profile.ProfileSiiPath, evidence, token), "user_profile");
            var infoPath = Path.Combine(request.Save.SaveDirectory, "info.sii");
            var info = SingleUnit(await ReadSaveMetadataAsync(infoPath, evidence, token), "save_container");
            var active = RequiredArray(profile, "active_mods", MaximumMods);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            mods = active.Select((value, index) =>
            {
                var separator = value.IndexOf('|');
                if (separator < 1) throw new InvalidDataException("Ungültige aktive Mod-Identität.");
                var identity = value[..separator];
                ValidateIdentity(identity);
                if (!identities.Add(identity)) throw new InvalidDataException("Aktive Mod-Identität ist mehrfach vorhanden.");
                return new ScsActiveMod(identity, value[(separator + 1)..], index);
            }).ToArray();
            var historical = new List<string>();
            foreach (var dependency in RequiredArray(info, "dependencies", MaximumEntries))
            {
                var parts = dependency.Split('|', 3);
                if (parts.Length != 3 || parts[1].Length == 0 || parts[0] is not ("mod" or "dlc" or "rdlc"))
                    throw new InvalidDataException("Nicht unterstützte Save-Abhängigkeit.");
                if (parts[0] == "mod") historical.Add(parts[1]);
            }
            if (!historical.SequenceEqual(mods.Select(mod => mod.Identity), StringComparer.Ordinal))
                Fail("Aktive Profil-Mods und Mod-Abhängigkeiten des ausgewählten Saves stimmen in Identität oder Reihenfolge nicht überein.");
        }
        catch (Exception ex) when (IsFailure(ex)) { Fail($"Profil-/Save-Konfiguration nicht verifiziert: {ex.Message}"); return Result(); }

        GameInstallation? installation;
        try
        {
            installation = await _detector.DetectAsync(game, request.PreferredInstallPath, token).ConfigureAwait(false);
            if (installation is null || installation.GameType != game) throw new InvalidDataException("Passende Spielinstallation fehlt.");
            var definition = GameDefinition.For(game);
            var installRoot = PlainPath(installation.InstallPath);
            var executable = PlainPath(installation.ExecutablePath);
            if (!SamePath(executable, Path.Combine(installRoot, definition.ExecutableRelativePath)))
                throw new InvalidDataException("Die Spiel-Executable gehört nicht zur ausgewählten Installation.");
            evidence.Physical(executable, MaximumExecutableBytes);
            gameVersion = NormalizeVersion(_installedVersionReader(executable));
            evidence.Physical(executable, MaximumExecutableBytes);
            var versionPath = Path.Combine(installRoot, "version.scs");
            using (var source = ScsDefinitionSource.Open(versionPath))
            {
                var version = SingleUnit(ScsDefinitionDocument.Parse(evidence.ReadMetadata(source, versionPath, "/version.sii")), "fs_pack_set");
                if (version.GetToken("application") != (game == GameType.Ets2 ? "eut2" : "ats"))
                    throw new InvalidDataException("version.scs gehört zu einem anderen Spiel.");
                if (NormalizeVersion(version.GetString("version")) != gameVersion)
                    throw new InvalidDataException("Executable und version.scs melden unterschiedliche Spielversionen.");
            }
            foreach (var path in Entries(installRoot, token).Where(path => File.Exists(path) && Path.GetExtension(path).Equals(".scs", StringComparison.OrdinalIgnoreCase)))
            {
                evidence.Physical(path);
                var dlc = Path.GetFileName(path).StartsWith("dlc_", StringComparison.OrdinalIgnoreCase);
                var identity = Path.GetFileName(path);
                var stock = StockArchives.Contains(identity);
                var reason = dlc ? "DLC-Datei vorhanden; aktuelle Steam-Berechtigung und Aktivierung sind nicht verifiziert." :
                    stock ? "Bekannte Originalspiel-Archivrolle; validierte Installation und übereinstimmende Spielversion." :
                    "Zusätzliches Installationsarchiv; seine tatsächliche Aktivierung ist nicht verifiziert.";
                if (dlc)
                {
                    try
                    {
                        using var source = ScsDefinitionSource.Open(path);
                        var manifests = source.GetFiles("/", token).Where(name => name.Count(c => c == '/') == 1 && name.EndsWith(".manifest.sii", StringComparison.Ordinal)).ToArray();
                        if (manifests.Length != 1) throw new InvalidDataException("Keine eindeutige DLC-Metadatendatei.");
                        var manifest = SingleUnit(ScsDefinitionDocument.Parse(evidence.ReadMetadata(source, path, manifests[0])), "dlc_package");
                        identity = manifest.GetToken("id") ?? throw new InvalidDataException("DLC-ID fehlt.");
                        var expectedGame = game == GameType.Ets2 ? "eut2" : "ats";
                        if (manifest.GetToken("game_id") != expectedGame) throw new InvalidDataException("DLC-Metadaten gehören zu einem anderen Spiel.");
                    }
                    catch (Exception ex) when (IsFailure(ex)) { reason += $" DLC-Metadaten nicht eindeutig: {ex.Message}"; }
                }
                AddLayer(layers, new(path, "/", dlc ? ScsPowertrainSourceKind.Dlc : ScsPowertrainSourceKind.Game,
                    identity, gameVersion, dlc ? 100 : 0, !dlc && stock, reason));
            }
        }
        catch (Exception ex) when (IsFailure(ex)) { Fail($"Spielinstallation nicht verifiziert: {ex.Message}"); return Result(); }

        string? localRoot = null;
        try
        {
            var roots = (request.ModDirectories ?? []).Where(item => item.Game == game)
                .Select(item => Path.TrimEndingDirectorySeparator(Path.GetFullPath(item.DirectoryPath))).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
            if (roots.Length > 1) throw new InvalidDataException("Mehrere lokale Mod-Ordner lassen die aktive Startkonfiguration offen.");
            localRoot = roots.SingleOrDefault() ?? Path.Combine(request.Profile.GameHomeDirectory, "mod");
        }
        catch (Exception ex) when (IsFailure(ex)) { Fail(ex.Message); }

        WorkshopContentSnapshot? workshop = null;
        if (mods.Any(mod => mod.Identity.StartsWith("mod_workshop_package.", StringComparison.Ordinal)))
        {
            try
            {
                workshop = await _workshop.ScanAsync(game, installation.InstallPath, token).ConfigureAwait(false);
                if (workshop.Game != game) throw new InvalidDataException("Workshop-Ergebnis gehört zu einem anderen Spiel.");
                if (workshop.Items.Count > MaximumEntries || workshop.ScannedContentRoots.Count > MaximumMods)
                    throw new InvalidDataException("Workshop-Inventar überschreitet das Leselimit.");
                // An incomplete scan cannot establish uniqueness across Steam libraries.
                if (workshop.Warnings.Count != 0) throw new InvalidDataException("Workshop-Erkennung ist nicht vollständig; eindeutige Paketzuordnung ist offen.");
            }
            catch (Exception ex) when (IsFailure(ex)) { Fail($"Workshop nicht verifiziert: {ex.Message}"); workshop = null; }
        }

        foreach (var mod in mods)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                string packagePath;
                var kind = ScsPowertrainSourceKind.LocalMod;
                if (mod.Identity.StartsWith("mod_workshop_package.", StringComparison.Ordinal))
                {
                    kind = ScsPowertrainSourceKind.Workshop;
                    if (workshop is null) throw new InvalidDataException("Workshop-Inventar fehlt.");
                    packagePath = ResolveWorkshop(mod.Identity, workshop, gameVersion, evidence, token);
                }
                else
                {
                    if (localRoot is null) throw new InvalidDataException("Aktiver lokaler Mod-Ordner ist nicht eindeutig.");
                    packagePath = ResolvePackage(localRoot, mod.Identity, token);
                }
                AddModLayers(packagePath, mod, kind, gameVersion, layers, evidence, token);
            }
            catch (Exception ex) when (IsFailure(ex)) { Fail($"Aktives Paket '{mod.Identity}' nicht verifiziert: {ex.Message}"); }
        }
        return Result();
    }

    private async Task<ScsDefinitionDocument> ReadSaveMetadataAsync(string path, Evidence evidence, CancellationToken token)
    {
        // The shared save codec caches by path/length/time. Give it a new, fingerprinted
        // copy so a same-size, same-time modification cannot return previously decoded text.
        var temporary = Path.Combine(Path.GetTempPath(), "NmcScsLauncher", "active-content", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var copy = Path.Combine(temporary, "metadata.sii");
        try
        {
            await using var input = new FileStream(PlainPath(path), FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var expectedHash = evidence.Physical(path, MaximumMetadataFileBytes);
            await using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                64 * 1024, FileOptions.Asynchronous))
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            await using (var copied = new FileStream(copy, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (Convert.ToHexString(await SHA256.HashDataAsync(copied, token).ConfigureAwait(false)) != expectedHash)
                    throw new InvalidDataException("Metadatenkopie stimmt nicht mit der geprüften Quelle überein.");
            }
            var document = await _codec.ReadAsync(copy, token).ConfigureAwait(false);
            evidence.Physical(path, MaximumMetadataFileBytes);
            if (document.Content.Length > MaximumMetadataFileBytes) throw new InvalidDataException("Profil-Metadaten überschreiten das Leselimit.");
            return ScsDefinitionDocument.Parse(document.Content);
        }
        finally
        {
            // Only this uniquely created file and its now-empty directory are removed.
            if (File.Exists(copy)) File.Delete(copy);
            Directory.Delete(temporary);
        }
    }

    private static string ResolveWorkshop(string identity, WorkshopContentSnapshot snapshot, string version, Evidence evidence, CancellationToken token)
    {
        const string prefix = "mod_workshop_package.";
        var hex = identity[prefix.Length..];
        if (hex.Length != 16 || !ulong.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var id) || id == 0)
            throw new InvalidDataException("Ungültige Workshop-Paket-ID.");
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in snapshot.Items.Where(item => item.Game == snapshot.Game && item.PublishedFileId == id)) roots.Add(PlainPath(item.ContentPath));
        foreach (var contentRoot in snapshot.ScannedContentRoots)
        {
            token.ThrowIfCancellationRequested();
            if (Path.GetFileName(Path.TrimEndingDirectorySeparator(contentRoot)) != GameDefinition.For(snapshot.Game).SteamAppId.ToString(CultureInfo.InvariantCulture))
                throw new InvalidDataException("Workshop-Wurzel ist nicht dem ausgewählten Spiel zugeordnet.");
            var path = Path.Combine(PlainPath(contentRoot), id.ToString(CultureInfo.InvariantCulture));
            if (Directory.Exists(path)) roots.Add(PlainPath(path));
        }
        if (roots.Count != 1) throw new InvalidDataException("Workshop-Paket fehlt oder ist in mehreren Bibliotheken vorhanden.");
        var root = roots.Single();
        var versionsPath = Path.Combine(root, "versions.sii");
        evidence.Physical(versionsPath, MaximumMetadataFileBytes);
        var versions = ScsDefinitionDocument.Parse(File.ReadAllText(versionsPath, new UTF8Encoding(false, true)));
        evidence.Physical(versionsPath, MaximumMetadataFileBytes);
        if (versions.Units.Count == 0 || versions.Units.Count > MaximumMods || versions.Units.Any(unit => unit.Type != "package_version_info"))
            throw new InvalidDataException("Nicht unterstützte Workshop-Versionstabelle.");
        var matches = new List<(string Name, bool Informational)>();
        var universal = new List<(string Name, bool Informational)>();
        foreach (var unit in versions.Units)
        {
            if (unit.AttributeNames.Any(name => name is not ("package_name" or "compatible_versions" or "informational")))
                throw new InvalidDataException("Unbekannte Workshop-Paketbedingung.");
            var name = unit.GetString("package_name") ?? throw new InvalidDataException("Workshop-Paketname fehlt.");
            ValidateIdentity(name);
            var informational = unit.GetToken("informational");
            if (informational is not (null or "true" or "false")) throw new InvalidDataException("Ungültige Informationspaket-Kennzeichnung.");
            var candidate = (name, informational == "true");
            var patterns = unit.GetStrings("compatible_versions");
            if (patterns.Count == 0) universal.Add(candidate);
            else if (patterns.Select(pattern => MatchesVersion(version, pattern)).ToArray().Any(match => match)) matches.Add(candidate);
        }
        var selected = matches.Count > 0 ? matches : universal;
        if (selected.Count != 1) throw new InvalidDataException("Workshop-Paketversion fehlt oder ist mehrdeutig.");
        if (selected[0].Informational) throw new InvalidDataException("Das passende Workshop-Paket dient nur zur Information und ist nicht ladbar.");
        return ResolvePackage(root, selected[0].Name, token);
    }

    private static void AddModLayers(string path, ScsActiveMod mod, ScsPowertrainSourceKind kind, string gameVersion,
        ICollection<ScsContentLayer> layers, Evidence evidence, CancellationToken token)
    {
        PlainPath(path);
        if (File.Exists(path)) evidence.Physical(path);
        using var source = ScsDefinitionSource.Open(path);
        var files = source.GetFiles("/", token);
        if (files.Any(file => file.Count(character => character == '/') == 1 &&
                (file.Equals("/base.zip", StringComparison.OrdinalIgnoreCase) || file.Equals("/base.scs", StringComparison.OrdinalIgnoreCase) ||
                 file.StartsWith("/dlc_", StringComparison.OrdinalIgnoreCase) &&
                 (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".scs", StringComparison.OrdinalIgnoreCase)))))
            throw new InvalidDataException("Verschachtelte Base-/DLC-Teilarchive werden noch nicht sicher aufgelöst.");
        var manifests = files.Where(file => file is "/manifest.sii" or "/base/manifest.sii").ToArray();
        if (manifests.Length != 1) throw new InvalidDataException("Eine eindeutige Mod-Manifestdatei ist erforderlich.");
        var manifest = SingleUnit(ScsDefinitionDocument.Parse(evidence.ReadMetadata(source, path, manifests[0])), "mod_package");
        if (manifest.AttributeNames.Any(name => !ManifestAttributes.Contains(name)))
            throw new InvalidDataException("Nicht unterstützte Mod-Manifestbedingung.");
        var version = manifest.GetString("package_version");
        if (string.IsNullOrWhiteSpace(version) || version.Length > 128 || version.Any(char.IsControl))
            throw new InvalidDataException("Mod-Paketversion fehlt oder ist ungültig.");
        var compatibility = manifest.GetStrings("compatible_versions");
        // Workshop activation uses versions.sii; the engine ignores this manifest
        // compatibility field for Workshop packages.
        if (kind == ScsPowertrainSourceKind.LocalMod && compatibility.Count > 0 &&
            !compatibility.Select(pattern => MatchesVersion(gameVersion, pattern)).ToArray().Any(match => match))
            throw new InvalidDataException("Mod-Paket unterstützt die installierte Spielversion nicht.");
        var dependencies = manifest.GetStrings("dlc_dependencies");
        if (dependencies.Any(dependency => string.IsNullOrWhiteSpace(dependency) || dependency.Any(char.IsControl)))
            throw new InvalidDataException("Ungültige DLC-Abhängigkeit im Mod-Manifest.");
        var available = dependencies.Count == 0;
        var reason = available ? "Aktives Profilpaket mit eindeutig aufgelöster Version." : "Mod-DLC-Abhängigkeiten vorhanden; Steam-Berechtigung und Aktivierung sind nicht verifiziert.";
        var priority = 1000 + mod.LoadOrder * 10;
        var roots = files.Select(file => file.Split('/')[1]).Distinct(StringComparer.Ordinal).ToArray();
        var hasBase = roots.Contains("base", StringComparer.Ordinal);
        // Preserve root content as a separate equal-priority layer if a package mixes layouts.
        if (!hasBase || files.Any(file => !file.StartsWith("/base/", StringComparison.Ordinal) &&
                !file.StartsWith("/dlc_", StringComparison.Ordinal) && file.Count(character => character == '/') > 1))
            AddLayer(layers, new(path, "/", kind, mod.Identity, version, priority, available, reason));
        if (hasBase) AddLayer(layers, new(path, "/base", kind, mod.Identity, version, priority, available, reason));
        foreach (var conditional in roots.Where(root => root.StartsWith("dlc_", StringComparison.Ordinal)))
            AddLayer(layers, new(path, "/" + conditional, kind, mod.Identity, version, priority + 1, false,
                "Bedingtes DLC-Teilpaket; seine aktuelle Aktivierung ist nicht verifiziert."));
    }

    private static void AddLayer(ICollection<ScsContentLayer> layers, ScsContentLayer layer)
    {
        if (layers.Count >= MaximumLayers) throw new InvalidDataException("Quellenebenen überschreiten das Leselimit.");
        layers.Add(layer);
    }

    private static string ResolvePackage(string directory, string identity, CancellationToken token)
    {
        ValidateIdentity(identity);
        var paths = Entries(PlainPath(directory), token).Where(path =>
            Directory.Exists(path) ? Path.GetFileName(path).Equals(identity, StringComparison.Ordinal) :
            (Path.GetExtension(path).Equals(".scs", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase)) &&
            Path.GetFileNameWithoutExtension(path).Equals(identity, StringComparison.Ordinal)).ToArray();
        if (paths.Length != 1) throw new InvalidDataException("Paketdatei/-ordner fehlt oder die Identität ist mehrdeutig.");
        return PlainPath(paths[0]);
    }

    private static void ValidateSelection(ScsEffectiveContentRequest request)
    {
        var profile = request.Profile;
        var save = request.Save;
        _ = GameDefinition.For(profile.GameType);
        if (profile.GameType != save.GameType || !SamePath(profile.ProfileDirectory, save.ProfileDirectory))
            throw new InvalidDataException("Profil und Save gehören nicht zum selben Spiel und Profil.");
        var directory = PlainPath(profile.ProfileDirectory);
        if (!SamePath(profile.ProfileSiiPath, Path.Combine(directory, "profile.sii")) ||
            !string.Equals(Path.GetFileName(directory), profile.ProfileDirectoryName, StringComparison.Ordinal))
            throw new InvalidDataException("Profilpfad und Profilidentität widersprechen sich.");
        if (profile.StorageKind is ScsProfileStorageKind.Local or ScsProfileStorageKind.SteamCloud)
        {
            var root = Path.Combine(profile.GameHomeDirectory, profile.StorageKind == ScsProfileStorageKind.Local ? "profiles" : "steam_profiles");
            if (!SamePath(Path.GetDirectoryName(directory)!, root)) throw new InvalidDataException("Profil liegt außerhalb seines Spiel-Home-Verzeichnisses.");
        }
        else if (profile.StorageKind != ScsProfileStorageKind.SteamCloudRemote ||
                 !ScsProfileSaveLocator.IsSteamCloudRemoteProfileDirectory(profile.GameType, directory))
            throw new InvalidDataException("Nicht unterstützter Profilspeicherpfad.");
        var saveDirectory = PlainPath(save.SaveDirectory);
        if (!SamePath(Path.GetDirectoryName(saveDirectory)!, Path.Combine(directory, "save")) ||
            !string.Equals(Path.GetFileName(saveDirectory), save.SaveDirectoryName, StringComparison.Ordinal) ||
            !SamePath(save.GameSiiPath, Path.Combine(saveDirectory, "game.sii")))
            throw new InvalidDataException("Savepfad und Auswahlidentität widersprechen sich.");
        PlainPath(save.GameSiiPath);
        if (!File.Exists(save.GameSiiPath)) throw new InvalidDataException("Ausgewählte game.sii fehlt.");
    }

    private static IReadOnlyList<string> RequiredArray(ScsDefinitionUnit unit, string name, int limit)
    {
        var declared = unit.GetDeclaredArrayCount(name);
        if (declared is not { } count || count < 0 || count > limit)
            throw new InvalidDataException($"Pflichtanzahl für {name} fehlt oder ist ungültig.");
        var items = unit.GetStrings(name);
        if (items.Count != count) throw new InvalidDataException($"Anzahl von {name} stimmt nicht mit den Einträgen überein.");
        return items;
    }

    private static ScsDefinitionUnit SingleUnit(ScsDefinitionDocument document, string type)
    {
        var units = document.Units.Where(unit => unit.Type == type).ToArray();
        if (units.Length != 1) throw new InvalidDataException($"Keine eindeutige {type}-Unit.");
        return units[0];
    }

    private static string NormalizeVersion(string? value)
    {
        if (value is null || !Version.TryParse(value, out var parsed) || parsed.Build < 0 || parsed.Revision < 0 ||
            value.Any(character => !(char.IsAsciiDigit(character) || character == '.')))
            throw new InvalidDataException("Keine unterstützte numerische Spielversion.");
        return parsed.ToString(4);
    }

    private static bool MatchesVersion(string version, string pattern)
    {
        var parts = pattern.Split('.');
        if (parts.Length is < 1 or > 4 || parts.Any(part => part.Length == 0) ||
            parts.Take(parts.Length - 1).Any(part => part == "*") ||
            parts.Any(part => part != "*" && !uint.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            throw new InvalidDataException("Nicht unterstütztes Versionsmuster.");
        var actual = version.Split('.');
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index] == "*") return true;
            if (uint.Parse(parts[index], CultureInfo.InvariantCulture) != uint.Parse(actual[index], CultureInfo.InvariantCulture)) return false;
        }
        return parts.Length == actual.Length;
    }

    private static string? ReadInstalledVersion(string path)
    {
        var info = FileVersionInfo.GetVersionInfo(path);
        return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
    }

    private static void ValidateIdentity(string identity)
    {
        if (identity.Length is < 1 or > 255 || identity is "." or ".." ||
            identity.Any(character => char.IsControl(character) || "\\/:*?\"<>|".Contains(character)))
            throw new InvalidDataException("Unsichere Paketidentität.");
    }

    private static IEnumerable<string> Entries(string path, CancellationToken token)
    {
        var count = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            token.ThrowIfCancellationRequested();
            if (++count > MaximumEntries) throw new InvalidDataException("Verzeichnisinventar überschreitet das Leselimit.");
            yield return entry;
        }
    }

    private static string PlainPath(string path)
    {
        var full = Path.GetFullPath(path);
        ScsDefinitionSource.CheckNoReparsePoints(full);
        return full;
    }
    private static bool SamePath(string left, string right) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
    private static bool IsFailure(Exception ex) => ScsPowertrainDiscoveryService.IsSourceFailure(ex) || ex is ScsSaveEditException;

    private sealed class Evidence(CancellationToken token)
    {
        private readonly Dictionary<string, ScsEvidenceFile> _files = new(StringComparer.Ordinal);
        internal IReadOnlyList<ScsEvidenceFile> Files => _files.Values.ToArray();

        internal string Physical(string path, long hashLimit = 0)
        {
            token.ThrowIfCancellationRequested();
            path = PlainPath(path);
            var before = new FileInfo(path);
            var length = before.Length;
            var time = new DateTimeOffset(before.LastWriteTimeUtc, TimeSpan.Zero);
            var hash = string.Empty;
            if (hashLimit > 0)
            {
                if (length > hashLimit) throw new InvalidDataException("Metadatendatei überschreitet das Leselimit.");
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                hash = Convert.ToHexString(SHA256.HashData(stream));
            }
            var after = new FileInfo(path);
            if (length != after.Length || time.UtcDateTime != after.LastWriteTimeUtc)
                throw new InvalidDataException("Quelldatei hat sich während der Prüfung geändert.");
            Store(new(path, length, time, hash));
            return hash;
        }

        internal string ReadMetadata(ScsDefinitionSource source, string path, string virtualPath)
        {
            if (Directory.Exists(path))
            {
                var file = Path.Combine(path, virtualPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                Physical(file, MaximumMetadataFileBytes);
                var text = source.ReadText(virtualPath, token);
                Physical(file, MaximumMetadataFileBytes);
                return text;
            }
            Physical(path);
            var content = source.ReadText(virtualPath, token);
            Physical(path);
            var fileInfo = new FileInfo(path);
            Store(new(Path.GetFullPath(path), fileInfo.Length, new(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))), virtualPath));
            return content;
        }

        private void Store(ScsEvidenceFile file)
        {
            var key = file.Path.ToUpperInvariant() + "\0" + file.VirtualPath;
            if (_files.TryGetValue(key, out var previous))
            {
                if (previous.Length != file.Length || previous.LastWriteTimeUtc != file.LastWriteTimeUtc ||
                    previous.Sha256.Length > 0 && file.Sha256.Length > 0 && previous.Sha256 != file.Sha256)
                    throw new InvalidDataException("Quellenbeleg hat sich während der Erkennung geändert.");
                if (previous.Sha256.Length > 0 && file.Sha256.Length == 0) return;
            }
            else if (_files.Count >= MaximumEntries) throw new InvalidDataException("Zu viele Quellenbelege.");
            _files[key] = file;
        }
    }
}
