using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

/// <summary>
/// Inventories local definition evidence. This service never authorizes or performs a save edit,
/// and never infers mod activation, mount order or DLC entitlement from file presence.
/// </summary>
public sealed class ScsPowertrainDiscoveryService : IScsPowertrainDiscoveryService
{
    private const int MaximumSources = 512;
    private const int MaximumCandidates = 30_000;
    private const int MaximumDirectories = 2_048;
    private const int MaximumDirectoryEntries = 4_096;
    private const int MaximumInventoryEntries = 64_000;
    private const int MaximumDiagnostics = 160;
    private readonly IGameInstallationDetector _installationDetector;
    private readonly IWorkshopContentLocator _workshopLocator;

    public ScsPowertrainDiscoveryService(IGameInstallationDetector installationDetector, IWorkshopContentLocator workshopLocator)
    {
        _installationDetector = installationDetector ?? throw new ArgumentNullException(nameof(installationDetector));
        _workshopLocator = workshopLocator ?? throw new ArgumentNullException(nameof(workshopLocator));
    }

    public Task<ScsPowertrainDiscoveryResult> DiscoverAsync(ScsPowertrainDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SaveCatalog);
        // Archive indexing and text reads are synchronous, bounded I/O. Keep them off the WPF thread.
        return Task.Run(() => DiscoverCoreAsync(request, cancellationToken), cancellationToken);
    }

    private async Task<ScsPowertrainDiscoveryResult> DiscoverCoreAsync(ScsPowertrainDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<string>();
        var sources = new Dictionary<string, Source>(StringComparer.OrdinalIgnoreCase);
        var inventoryBudget = new InventoryBudget();
        void Warn(string message)
        {
            if (diagnostics.Count < MaximumDiagnostics) diagnostics.Add(message);
            else if (diagnostics.Count == MaximumDiagnostics) diagnostics.Add("Weitere Hinweise wurden wegen des Anzeigelimits zusammengefasst.");
        }
        void AddSource(string path, ScsPowertrainSourceKind kind, string name)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var fullPath = RequirePlainPath(path);
                if (sources.ContainsKey(fullPath)) return;
                if (sources.Count >= MaximumSources) { Warn("Quellenlimit erreicht; der Lesekatalog ist unvollständig."); return; }
                sources.Add(fullPath, new(fullPath, new(kind, name, fullPath)));
            }
            catch (Exception exception) when (IsSourceFailure(exception)) { Warn($"Quelle übersprungen ({name}): {exception.Message}"); }
        }

        GameInstallation? installation = null;
        try
        {
            installation = await _installationDetector.DetectAsync(request.Game, request.PreferredInstallPath, cancellationToken).ConfigureAwait(false);
            if (installation is not null && installation.GameType == request.Game)
            {
                var root = RequirePlainPath(installation.InstallPath);
                foreach (var path in LimitedEntries(root, inventoryBudget, Warn, cancellationToken))
                {
                    if (!File.Exists(path) || !Path.GetExtension(path).Equals(".scs", StringComparison.OrdinalIgnoreCase)) continue;
                    var fileName = Path.GetFileName(path);
                    AddSource(path, fileName.StartsWith("dlc_", StringComparison.OrdinalIgnoreCase) ? ScsPowertrainSourceKind.Dlc : ScsPowertrainSourceKind.Game, fileName);
                }
            }
            else Warn("Keine passende Spielinstallation gefunden; vorhandene Save-Komponenten bleiben verfügbar.");
        }
        catch (Exception exception) when (IsSourceFailure(exception)) { Warn($"Spielinstallation konnte nicht gelesen werden: {exception.Message}"); }

        if (!string.IsNullOrWhiteSpace(request.GameHomeDirectory))
            InventoryPackages(Path.Combine(request.GameHomeDirectory, "mod"), ScsPowertrainSourceKind.LocalMod, "Profil-Mods", inventoryBudget, AddSource, Warn, cancellationToken);

        if (request.ModDirectories is not null)
        {
            foreach (var directory in request.ModDirectories.Where(item => item.Game == request.Game).Take(MaximumSources))
                InventoryPackages(directory.DirectoryPath, ScsPowertrainSourceKind.LocalMod, directory.Name, inventoryBudget, AddSource, Warn, cancellationToken);
        }

        try
        {
            var workshop = await _workshopLocator.ScanAsync(request.Game, installation?.GameType == request.Game ? installation.InstallPath : request.PreferredInstallPath,
                cancellationToken).ConfigureAwait(false);
            if (workshop.Game != request.Game) Warn("Workshop-Ergebnis gehört zu einem anderen Spiel und wurde verworfen.");
            else
            {
                foreach (var warning in workshop.Warnings.Take(MaximumDiagnostics)) Warn(warning);
                foreach (var item in workshop.Items.Where(item => item.Game == request.Game).Take(MaximumSources))
                {
                    Warn($"{item.DisplayName}: lokale Pakete werden nur inventarisiert; Version, Profilaktivierung und Reihenfolge sind nicht verifiziert.");
                    InventoryPackages(item.ContentPath, ScsPowertrainSourceKind.Workshop, item.DisplayName, inventoryBudget, AddSource, Warn, cancellationToken);
                }
            }
        }
        catch (Exception exception) when (IsSourceFailure(exception)) { Warn($"Workshop konnte nicht gelesen werden: {exception.Message}"); }

        var candidates = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, items) in new[] { (ScsPowertrainKind.Engine, request.SaveCatalog.Engines), (ScsPowertrainKind.Transmission, request.SaveCatalog.Transmissions) })
        {
            foreach (var saved in items.Take(MaximumCandidates))
            {
                if (!TryDefinitionPath(saved.DataPath, out var parsedKind, out var model) || kind != parsedKind) continue;
                var candidate = GetCandidate(candidates, saved.DataPath, kind, model);
                candidate.Observed = true;
                candidate.IsCurrent |= saved.IsCurrent;
                candidate.Sources.Add(new(ScsPowertrainSourceKind.Save, $"Save-Truck {saved.SourceTruckId}", saved.SourceTruckId));
            }
        }

        var scannedCandidates = 0;
        foreach (var source in sources.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scannedCandidates >= MaximumCandidates) { Warn("Komponentenlimit erreicht; der Lesekatalog ist unvollständig."); break; }
            try
            {
                using var reader = ScsDefinitionSource.Open(source.Path);
                // Modern mod archives may contain conditional base/dlc_* packages. Their
                // mount roots are visible evidence only; entitlement and activation remain unknown.
                var inspectPackages = source.Provenance.Kind is ScsPowertrainSourceKind.LocalMod or ScsPowertrainSourceKind.Workshop && File.Exists(source.Path);
                foreach (var physicalPath in reader.GetFiles(inspectPackages ? "/" : "/def/vehicle/truck", cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = physicalPath;
                    var mountRoot = string.Empty;
                    if (inspectPackages)
                    {
                        var separator = physicalPath.IndexOf('/', 1);
                        var first = separator > 0 ? physicalPath[1..separator] : string.Empty;
                        if (first.Equals("base", StringComparison.OrdinalIgnoreCase) || first.StartsWith("dlc_", StringComparison.OrdinalIgnoreCase))
                        {
                            mountRoot = "/" + first;
                            path = physicalPath[separator..];
                        }
                    }
                    if (!TryDefinitionPath(path, out var kind, out var model)) continue;
                    if (++scannedCandidates > MaximumCandidates) break;
                    var candidate = GetCandidate(candidates, path, kind, model);
                    candidate.Sources.Add(mountRoot.Length == 0 ? source.Provenance : source.Provenance with
                    {
                        Name = source.Provenance.Name + " / " + mountRoot[1..] + " (Bedingtes Paket)",
                        Location = source.Provenance.Location + "!" + mountRoot
                    });
                    var definitionSource = mountRoot.Length == 0 ? reader : new MountedDefinitionSource(reader, mountRoot);
                    var parsed = ScsPowertrainDefinitionParser.Read(definitionSource, path,
                        kind == ScsPowertrainKind.Engine ? "accessory_engine_data" : "accessory_transmission_data", cancellationToken);
                    candidate.Definitions.Add(parsed);
                }
            }
            catch (Exception exception) when (IsSourceFailure(exception))
            {
                Warn($"{source.Provenance.Name}: Quelle nicht vollständig lesbar ({exception.Message}). Save-Auswahl bleibt erhalten.");
            }
        }

        var duplicateIds = candidates.Values.SelectMany(candidate => candidate.Definitions.Where(definition => definition.UnitId is not null)
                .Select(definition => (definition.UnitId, candidate.Path)))
            .GroupBy(item => item.UnitId!, StringComparer.Ordinal)
            .Where(group => group.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        var activeModel = request.SaveCatalog.TruckModelRoot.Trim('/').Split('/').LastOrDefault() ?? string.Empty;
        var result = candidates.Values.Select(candidate => BuildResult(request.Game, activeModel, candidate, duplicateIds))
            .OrderByDescending(item => item.IsCurrent)
            .ThenBy(item => item.TruckModel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DataPath, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(result, diagnostics.ToArray());
    }

    private static void InventoryPackages(string root, ScsPowertrainSourceKind kind, string label,
        InventoryBudget budget,
        Action<string, ScsPowertrainSourceKind, string> add, Action<string> warn, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root)) return;
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++budget.Directories > MaximumDirectories || budget.Entries >= MaximumInventoryEntries)
            { warn($"{label}: Inventarlimit erreicht; nicht alle Pakete inventarisiert."); break; }
            var (path, depth) = pending.Pop();
            try
            {
                RequirePlainPath(path);
                var containsDefinitions = Directory.Exists(Path.Combine(path, "def"));
                if (containsDefinitions) add(path, kind, $"{label} / {Path.GetRelativePath(root, path)}");
                foreach (var child in LimitedEntries(path, budget, warn, cancellationToken))
                {
                    var name = Path.GetFileName(child);
                    if (File.Exists(child))
                    {
                        var extension = Path.GetExtension(child);
                        if (extension.Equals(".scs", StringComparison.OrdinalIgnoreCase) || extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                            add(child, kind, $"{label} / {Path.GetRelativePath(root, child)}");
                    }
                    else if (Directory.Exists(child) && !name.Equals("def", StringComparison.OrdinalIgnoreCase) &&
                             (!containsDefinitions || name.Equals("base", StringComparison.OrdinalIgnoreCase) || name.StartsWith("dlc_", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (depth < 3) pending.Push((child, depth + 1));
                        else warn($"{label}: tiefer verschachteltes Paket wurde übersprungen.");
                    }
                }
            }
            catch (Exception exception) when (IsSourceFailure(exception)) { warn($"{label}: Verzeichnis übersprungen ({exception.Message})."); }
        }
    }

    private static IEnumerable<string> LimitedEntries(string directory, InventoryBudget budget, Action<string> warn, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++count > MaximumDirectoryEntries || ++budget.Entries > MaximumInventoryEntries)
            { warn("Verzeichniseintragslimit erreicht; die Erkennung ist unvollständig."); yield break; }
            yield return entry;
        }
    }

    private static string RequirePlainPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        for (var current = fullPath; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Links und Junctions werden nicht verfolgt.");
        }
        return fullPath;
    }

    private static bool TryDefinitionPath(string path, out ScsPowertrainKind kind, out string model)
    {
        kind = default; model = string.Empty;
        if (!path.StartsWith('/') || path.Contains('\\') || path.Contains(':')) return false;
        var segments = path.Split('/');
        if (segments.Length != 7 || segments.Any(segment => segment is "." or "..") ||
            !segments[1].Equals("def", StringComparison.OrdinalIgnoreCase) ||
            !segments[2].Equals("vehicle", StringComparison.OrdinalIgnoreCase) ||
            !segments[3].Equals("truck", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(segments[4]) || !segments[6].EndsWith(".sii", StringComparison.OrdinalIgnoreCase)) return false;
        if (segments[5].Equals("engine", StringComparison.OrdinalIgnoreCase)) kind = ScsPowertrainKind.Engine;
        else if (segments[5].Equals("transmission", StringComparison.OrdinalIgnoreCase)) kind = ScsPowertrainKind.Transmission;
        else return false;
        model = segments[4];
        return true;
    }

    private static Candidate GetCandidate(Dictionary<string, Candidate> candidates, string path, ScsPowertrainKind kind, string model)
    {
        if (!candidates.TryGetValue(path, out var candidate)) candidates.Add(path, candidate = new(path, kind, model));
        return candidate;
    }

    private static ScsDiscoveredPowertrain BuildResult(GameType game, string activeModel, Candidate candidate, HashSet<string> duplicateIds)
    {
        var definitions = candidate.Definitions;
        var names = definitions.Where(item => item.Name is not null).Select(item => item.Name!).Distinct(StringComparer.Ordinal).ToArray();
        var name = names.Length == 1 ? names[0] : Path.GetFileNameWithoutExtension(candidate.Path);
        var compatibility = ScsPowertrainCompatibility.ModelMatchUnverified;
        var reason = "Truck-Modell stimmt überein; Zubehörregeln, Abhängigkeiten und aktive Quellen sind nicht vollständig verifiziert. Nur lesen.";
        if (definitions.Any(item => item.Ambiguous || item.UnitId is not null && duplicateIds.Contains(item.UnitId)) ||
            definitions.Where(item => item.Error is null).Select(item => item.Fingerprint).Distinct(StringComparer.Ordinal).Skip(1).Any())
        {
            compatibility = ScsPowertrainCompatibility.Ambiguous;
            reason = "Mehrdeutige Definitionen oder Unit-IDs; aktive Mod-/Paket-Reihenfolge ist unbekannt. Kein Wechsel aus dem Lesekatalog.";
        }
        else if (definitions.Any(item => item.Error is not null))
        {
            compatibility = ScsPowertrainCompatibility.Unresolved;
            reason = definitions.First(item => item.Error is not null).Error! + " Nur lesen.";
        }
        else if (!candidate.Model.Equals(activeModel, StringComparison.OrdinalIgnoreCase))
        {
            compatibility = ScsPowertrainCompatibility.DifferentModel;
            reason = "Definition gehört zu einem anderen Truck-Modell. Kein Wechsel aus dem Lesekatalog.";
        }
        else if (candidate.Observed)
        {
            compatibility = ScsPowertrainCompatibility.ObservedInSave;
            reason = "Im ausgewählten Save vorhanden; die bisherige Save-Auswahl bleibt separat verfügbar. Externe Verfügbarkeit ist nicht bestätigt.";
        }
        if (candidate.Sources.Any(source => source.Kind == ScsPowertrainSourceKind.Dlc))
            reason += " DLC-Datei vorhanden; Besitz und Aktivierung sind nicht verifiziert.";
        if (candidate.Sources.Any(source => source.Kind is ScsPowertrainSourceKind.LocalMod or ScsPowertrainSourceKind.Workshop))
            reason += " Mod-Version, Profilaktivierung und Priorität sind nicht verifiziert.";
        return new(game, candidate.Kind, candidate.Path, name, candidate.Model, candidate.IsCurrent, compatibility, reason,
            candidate.Sources.Distinct().ToArray());
    }

    internal static bool IsSourceFailure(Exception exception) => exception is IOException or InvalidDataException or UnauthorizedAccessException or
        NotSupportedException or ArgumentException or FormatException or OverflowException;

    private sealed record Source(string Path, ScsPowertrainSource Provenance);
    private sealed class InventoryBudget { internal int Directories; internal int Entries; }
    private sealed class MountedDefinitionSource(ScsDefinitionSource inner, string root) : ScsDefinitionSource
    {
        public override IReadOnlyList<string> GetFiles(string virtualDirectory, CancellationToken cancellationToken = default) =>
            inner.GetFiles(root + virtualDirectory, cancellationToken).Select(path => path[root.Length..]).ToArray();
        public override string ReadText(string virtualPath, CancellationToken cancellationToken = default) =>
            inner.ReadText(root + virtualPath, cancellationToken);
    }
    private sealed class Candidate(string path, ScsPowertrainKind kind, string model)
    {
        internal string Path { get; } = path;
        internal ScsPowertrainKind Kind { get; } = kind;
        internal string Model { get; } = model;
        internal bool Observed;
        internal bool IsCurrent;
        internal List<ScsPowertrainSource> Sources { get; } = [];
        internal List<ScsPowertrainDefinitionParser.Definition> Definitions { get; } = [];
    }
}
