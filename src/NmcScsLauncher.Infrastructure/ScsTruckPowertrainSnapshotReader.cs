using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed record ScsTruckPowertrainReadResult(ScsTruckPowertrainSnapshot Snapshot, string Content);

/// <summary>Reads a detached, byte-bound view of the selected owned truck. It never writes a save.</summary>
public sealed class ScsTruckPowertrainSnapshotReader(IScsSaveCodec codec)
{
    private const int MaximumSaveBytes = 128 * 1024 * 1024;
    private const int MaximumAccessories = 4096;
    private static readonly Regex UnitHeader = new(@"^(?<type>[A-Za-z0-9_]+)\s*:\s*(?<id>[A-Za-z0-9_.]+)\s*\{$", RegexOptions.Compiled);
    private static readonly Regex Reference = new(@"^\.?[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*$", RegexOptions.Compiled);
    private static readonly Regex AccessoryArrayField = new(@"^\s*accessories\s*(?:\[|:)", RegexOptions.Compiled);
    private static readonly IReadOnlyDictionary<string, HashSet<string>> KnownAccessoryFields = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
    {
        ["vehicle_accessory"] = Fields("data_path", "refund"),
        ["vehicle_addon_accessory"] = Fields("data_path", "refund", "paint_color", "slot_name", "slot_hookup"),
        ["vehicle_drv_plate_accessory"] = Fields("data_path", "refund", "paint_color", "slot_name", "slot_hookup", "text"),
        ["vehicle_paint_job_accessory"] = Fields("data_path", "refund", "base_color", "flake_color", "flip_color", "mask_b_color", "mask_g_color", "mask_r_color"),
        ["vehicle_wheel_accessory"] = Fields("data_path", "refund", "offset", "paint_color")
    };

    public async Task<ScsTruckPowertrainReadResult> ReadAsync(ScsSaveReference save, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        cancellationToken.ThrowIfCancellationRequested();
        var path = ValidateSavePath(save);
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "NmcScsLauncher", "truck-powertrain", Guid.NewGuid().ToString("N"));
        var temporaryPath = Path.Combine(temporaryDirectory, "game.sii");
        try
        {
            ScsDefinitionSource.CheckNoReparsePoints(path);
            Directory.CreateDirectory(temporaryDirectory);
            ScsDefinitionSource.CheckNoReparsePoints(temporaryDirectory);
            var before = new FileInfo(path);
            var timestamp = before.LastWriteTimeUtc;
            var length = before.Length;
            string sha;
            // The original is locked only during the bounded copy, never throughout decoding or UI preview.
            await using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                ValidateSize(input.Length);
                if (input.Length != length) throw Changed();
                sha = await HashAndCopyAsync(input, output, cancellationToken).ConfigureAwait(false);
            }
            var decoded = await codec.ReadAsync(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (decoded.Format == ScsSaveDocumentFormat.UnsupportedBinary)
                throw new ScsSaveEditException("Der ausgewählte Save konnte nicht als SII-Text gelesen werden.");
            ValidateTextSize(decoded.Content);
            ScsDefinitionSource.CheckNoReparsePoints(path);
            await using (var current = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous))
            {
                var after = new FileInfo(path);
                if (current.Length != length || after.LastWriteTimeUtc != timestamp ||
                    !string.Equals(sha, await HashAndCopyAsync(current, null, cancellationToken).ConfigureAwait(false), StringComparison.Ordinal))
                    throw Changed();
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new(Parse(save, sha, decoded.Content), decoded.Content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            throw new ScsSaveEditException("Der ausgewählte Truck-Save konnte nicht eindeutig und unverändert gelesen werden: " + exception.Message, exception);
        }
        finally
        {
            // Only the unique files created by this read are removed.
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { if (Directory.Exists(temporaryDirectory)) Directory.Delete(temporaryDirectory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public static ScsTruckPowertrainSnapshot Parse(ScsSaveReference save, string sha256, string content)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(content);
        var path = ValidateSavePath(save);
        if (sha256 is null || sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ScsSaveEditException("Der Save-Snapshot benötigt einen gültigen SHA-256-Fingerabdruck.");
        ValidateTextSize(content);
        try
        {
            var index = new SaveIndex(content);
            var economy = index.SingleType("economy");
            var player = index.Required(RequiredReference(economy, "player"), "player");
            var trucks = ReadReferences(player, "trucks");
            if (trucks.Count > 10000) throw new InvalidDataException("Die Truck-Besitzliste überschreitet das Leselimit.");
            foreach (var owned in trucks) index.Required(owned, "vehicle");
            var legacy = OptionalReference(player, "assigned_truck");
            var assignmentId = OptionalReference(player, "assigned_vehicles");
            var active = legacy;
            if (assignmentId is not null)
            {
                var assignment = index.Required(assignmentId, "player_vehicles");
                active = RequiredReference(assignment, "vehicle");
                var trailer = OptionalReference(assignment, "trailer");
                if (trailer is not null) index.Required(trailer, "trailer");
                if (legacy is not null && !string.Equals(legacy, active, StringComparison.Ordinal))
                    throw new InvalidDataException("Die aktiven Truck-Referenzen widersprechen sich.");
            }
            if (active is null || !trucks.Contains(active, StringComparer.Ordinal))
                throw new InvalidDataException("Der aktive Truck ist nicht eindeutig als eigener Truck ausgewiesen.");
            var truck = index.Required(active, "vehicle");
            var accessoryIds = ReadReferences(truck, "accessories");
            if (accessoryIds.Count is < 3 or > MaximumAccessories)
                throw new InvalidDataException("Die Zubehörliste des aktiven Trucks hat eine nicht unterstützte Größe.");
            var selectedIds = accessoryIds.ToHashSet(StringComparer.Ordinal);
            foreach (var owner in index.AccessoryOwners)
            {
                if (owner.Id == active) continue;
                if (ReadReferences(index.Required(owner.Id), "accessories").Any(selectedIds.Contains))
                    throw new InvalidDataException("Zubehör des aktiven Trucks wird zusätzlich von einer anderen Unit verwendet.");
            }

            var accessories = accessoryIds.Select(id => ReadAccessory(index.Required(id))).ToArray();
            var data = accessories.Where(accessory => IsTruckDataPath(accessory.DataPath)).ToArray();
            if (data.Length != 1)
                throw new InvalidDataException("Der Truck benötigt genau eine unabhängige Truck-Datendefinition.");
            var root = data[0].DataPath[..^"/data.sii".Length];
            var engines = accessories.Where(accessory => IsCategoryPath(accessory.DataPath, "engine")).ToArray();
            var transmissions = accessories.Where(accessory => IsCategoryPath(accessory.DataPath, "transmission")).ToArray();
            if (engines.Length != 1 || transmissions.Length != 1)
                throw new InvalidDataException("Der aktive Truck benötigt genau einen Motor und ein Getriebe.");
            if (data[0].UnitType != "vehicle_accessory" || engines[0].UnitType != "vehicle_accessory" || transmissions[0].UnitType != "vehicle_accessory")
                throw new InvalidDataException("Truck-Daten, Motor und Getriebe benötigen den gespeicherten Unit-Typ vehicle_accessory.");
            ValidateComponentPath(engines[0].DataPath, root, "engine");
            ValidateComponentPath(transmissions[0].DataPath, root, "transmission");
            return new(save.GameType, path, sha256.ToUpperInvariant(), active, root, data[0].UnitId,
                engines[0].UnitId, transmissions[0].UnitId, engines[0].DataPath, transmissions[0].DataPath,
                Array.AsReadOnly(accessories));
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
        {
            throw new ScsSaveEditException("Der aktive Truck kann nicht sicher aufgelöst werden: " + exception.Message, exception);
        }
    }

    /// <summary>Pure text transformation for a separately verified write transaction; performs no file IO.</summary>
    public static string BuildUpdatedContent(ScsTruckPowertrainReadResult read, string enginePath, string transmissionPath)
    {
        ArgumentNullException.ThrowIfNull(read);
        var snapshot = read.Snapshot;
        var save = ReferenceFor(snapshot);
        var original = Parse(save, snapshot.SaveSha256, read.Content);
        if (original.TruckId != snapshot.TruckId || original.TruckModelRoot != snapshot.TruckModelRoot ||
            original.EngineAccessoryId != snapshot.EngineAccessoryId || original.TransmissionAccessoryId != snapshot.TransmissionAccessoryId ||
            original.CurrentEnginePath != snapshot.CurrentEnginePath || original.CurrentTransmissionPath != snapshot.CurrentTransmissionPath)
            throw new ScsSaveEditException("Der Truck-Snapshot passt nicht zum vorliegenden Save-Text.");
        if (original.Accessories.Any(accessory => accessory.UnsupportedReasons.Count != 0))
            throw new ScsSaveEditException("Der Truck enthält noch nicht unterstützte Zubehörregeln.");
        try
        {
            ValidateComponentPath(enginePath, original.TruckModelRoot, "engine");
            ValidateComponentPath(transmissionPath, original.TruckModelRoot, "transmission");
            var document = ScsSiiUnitDocument.Parse(read.Content);
            document = document.SetRequiredUnitScalar(document.GetRequiredUniqueUnit(original.EngineAccessoryId), "data_path", "\"" + enginePath + "\"");
            document = document.SetRequiredUnitScalar(document.GetRequiredUniqueUnit(original.TransmissionAccessoryId), "data_path", "\"" + transmissionPath + "\"");
            var updated = document.ToText();
            var after = Parse(save, snapshot.SaveSha256, updated);
            if (after.TruckId != original.TruckId || after.TruckDataAccessoryId != original.TruckDataAccessoryId ||
                !after.Accessories.Select(accessory => (accessory.UnitId, accessory.UnitType)).SequenceEqual(original.Accessories.Select(accessory => (accessory.UnitId, accessory.UnitType))))
                throw new InvalidDataException("Die Zubehörstruktur wurde bei der Textänderung verändert.");
            return updated;
        }
        catch (InvalidDataException exception) { throw new ScsSaveEditException(exception.Message, exception); }
    }

    private static ScsSaveReference ReferenceFor(ScsTruckPowertrainSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(snapshot.SaveFilePath)!;
        var profile = Path.GetDirectoryName(Path.GetDirectoryName(directory))!;
        return new(snapshot.Game, profile, directory, Path.GetFileName(directory), string.Empty, ScsSaveKind.Unknown,
            snapshot.SaveFilePath, default);
    }

    private static ScsTruckAccessorySnapshot ReadAccessory(ScsDefinitionUnit unit)
    {
        var path = unit.GetString("data_path") ?? throw new InvalidDataException("Eine Zubehör-Unit hat keinen eindeutigen data_path.");
        ValidateDefinitionPath(path);
        var reasons = new List<string>();
        if (!KnownAccessoryFields.TryGetValue(unit.Type, out var fields))
            reasons.Add($"Zubehörtyp '{unit.Type}' ist unbekannt.");
        else
        {
            foreach (var field in unit.AttributeNames.Where(field => !fields.Contains(field)))
                reasons.Add($"Zubehörfeld '{field}' wird noch nicht unterstützt.");
        }
        var hasSlots = unit.HasAttribute("slot_name");
        var hasHookups = unit.HasAttribute("slot_hookup");
        if (hasSlots != hasHookups) reasons.Add("Slot-/Hookup-Zuordnung ist unvollständig.");
        if (hasSlots || hasHookups)
        {
            // Actual saves use unquoted slot tokens and quoted hookup IDs. Preserve both
            // typed representations while the nonempty relationship remains unsupported.
            var slots = unit.GetValues("slot_name");
            var hookups = unit.GetValues("slot_hookup");
            if (slots.Count != hookups.Count || slots.Count > 0 || hookups.Count > 0)
                reasons.Add("Nicht leere Slot-/Hookup-Zuordnungen können noch nicht vollständig geprüft werden.");
            if (unit.GetDeclaredArrayCount("slot_name") is null || unit.GetDeclaredArrayCount("slot_hookup") is null)
                reasons.Add("Slot-/Hookup-Zuordnungen haben keine eindeutige deklarierte Anzahl.");
        }
        return new(unit.Id, unit.Type, path, reasons.AsReadOnly());
    }

    private static IReadOnlyList<string> ReadReferences(ScsDefinitionUnit unit, string field)
    {
        if (unit.GetDeclaredArrayCount(field) is not { } count || count > 10000)
            throw new InvalidDataException($"Referenzliste '{field}' benötigt eine begrenzte deklarierte Anzahl.");
        var references = unit.GetTokens(field);
        if (references.Count != count || references.Any(value => !IsReference(value)) || references.Distinct(StringComparer.Ordinal).Count() != count)
            throw new InvalidDataException($"Referenzliste '{field}' ist unvollständig, doppelt oder ungültig.");
        return references;
    }

    private static string RequiredReference(ScsDefinitionUnit unit, string field) => OptionalReference(unit, field) ??
        throw new InvalidDataException($"Die aktive Referenz '{field}' fehlt.");
    private static string? OptionalReference(ScsDefinitionUnit unit, string field)
    {
        var token = unit.GetToken(field);
        if (token is null or "null") return null;
        if (!IsReference(token)) throw new InvalidDataException($"Referenz '{field}' ist ungültig.");
        return token;
    }
    private static bool IsReference(string value) => value != "null" && value.Length <= 256 && Reference.IsMatch(value);
    private static HashSet<string> Fields(params string[] values) => new(values, StringComparer.Ordinal);
    private static bool IsTruckDataPath(string path) => path.StartsWith("/def/vehicle/truck/", StringComparison.Ordinal) &&
        path.EndsWith("/data.sii", StringComparison.Ordinal) && path.Count(character => character == '/') == 5;
    private static bool IsCategoryPath(string path, string category)
    {
        var parts = path.Split('/');
        return parts.Length >= 7 && parts[1] == "def" && parts[2] == "vehicle" && parts[3] == "truck" && parts[5] == category;
    }
    private static void ValidateComponentPath(string path, string root, string category)
    {
        ValidateDefinitionPath(path);
        if (!path.StartsWith(root + "/" + category + "/", StringComparison.Ordinal))
            throw new InvalidDataException("Die Antriebsdefinition gehört nicht zum unabhängig ermittelten Truck-Modell.");
    }
    private static void ValidateDefinitionPath(string path)
    {
        ScsDefinitionSource.ValidatePath(path);
        if (!path.StartsWith("/def/", StringComparison.Ordinal) || !path.EndsWith(".sii", StringComparison.Ordinal))
            throw new InvalidDataException("Zubehör benötigt einen vollständigen SII-Definitionspfad.");
    }
    private static string ValidateSavePath(ScsSaveReference save)
    {
        try
        {
            if (!Enum.IsDefined(save.GameType)) throw new InvalidDataException("Unbekanntes Spiel.");
            var profile = Path.GetFullPath(save.ProfileDirectory);
            var directory = Path.GetFullPath(save.SaveDirectory);
            var path = Path.GetFullPath(save.GameSiiPath);
            if (!string.Equals(Path.GetDirectoryName(directory), Path.Combine(profile, "save"), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(directory), save.SaveDirectoryName, StringComparison.Ordinal) ||
                !string.Equals(path, Path.Combine(directory, "game.sii"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Profil, Save-Verzeichnis und game.sii gehören nicht zur gleichen Auswahl.");
            return path;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or InvalidDataException)
        { throw new ScsSaveEditException("Ungültige Save-Auswahl: " + exception.Message, exception); }
    }
    private static void ValidateSize(long size)
    {
        if (size is <= 0 or > MaximumSaveBytes) throw new ScsSaveEditException("Der Save überschreitet das unterstützte Leselimit von 128 MiB oder ist leer.");
    }
    private static void ValidateTextSize(string content)
    {
        ValidateSize(content.Length);
        ValidateSize(Encoding.UTF8.GetByteCount(content));
    }
    private static ScsSaveEditException Changed() => new("Der Save wurde während des Lesens verändert. Bitte erneut laden.");
    private static async Task<string> HashAndCopyAsync(Stream input, Stream? output, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        var total = 0L;
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
        {
            total += read; ValidateSize(total);
            hash.AppendData(buffer, 0, read);
            if (output is not null) await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
        ValidateSize(total);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>
    /// The existing save parser handles the large document. A cheap structural pass prevents
    /// ignored/duplicate unit headers; only relevant individual units use the strict metadata AST.
    /// </summary>
    private sealed class SaveIndex
    {
        private readonly string[] _lines;
        private readonly Dictionary<string, ScsSiiUnit> _units = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScsDefinitionUnit> _parsed = new(StringComparer.Ordinal);
        internal List<ScsSiiUnit> AccessoryOwners { get; } = [];

        internal SaveIndex(string content)
        {
            _ = ScsSiiUnitDocument.Parse(content);
            _lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var state = 0;
            ScsSiiUnit? current = null;
            var ownsAccessories = false;
            for (var number = 0; number < _lines.Length; number++)
            {
                var line = _lines[number].Trim().TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (state == 0 && line is "SiiNunit" or "SiiNunit {") { state = line.EndsWith('{') ? 2 : 1; continue; }
                if (state == 1 && line == "{") { state = 2; continue; }
                if (state == 2)
                {
                    if (line == "}") { state = 4; continue; }
                    var match = UnitHeader.Match(line);
                    if (!match.Success || !IsReference(match.Groups["id"].Value)) throw new InvalidDataException("Nicht unterstützte oder uneindeutige SII-Unit-Deklaration.");
                    current = new(match.Groups["type"].Value, match.Groups["id"].Value, number, -1);
                    ownsAccessories = false;
                    state = 3;
                    continue;
                }
                if (state == 3)
                {
                    if (line == "}")
                    {
                        current = current! with { EndLine = number };
                        if (_units.Count >= 500000 || !_units.TryAdd(current.Id, current)) throw new InvalidDataException("Doppelte SII-Unit oder Unit-Leselimit überschritten.");
                        if (ownsAccessories) AccessoryOwners.Add(current);
                        state = 2;
                    }
                    else
                    {
                        ValidateBodyLine(line);
                        ownsAccessories |= AccessoryArrayField.IsMatch(line);
                    }
                    continue;
                }
                throw new InvalidDataException("Nicht unterstützte SII-Dokumentstruktur.");
            }
            if (state != 4) throw new InvalidDataException("Unvollständiges SII-Dokument.");
        }

        internal ScsDefinitionUnit SingleType(string type)
        {
            var matches = _units.Values.Where(unit => unit.Type == type).Take(2).ToArray();
            if (matches.Length != 1) throw new InvalidDataException($"Unit-Typ '{type}' ist nicht eindeutig.");
            return Required(matches[0].Id, type);
        }
        internal ScsDefinitionUnit Required(string id, string? type = null)
        {
            if (!_units.TryGetValue(id, out var unit) || type is not null && unit.Type != type)
                throw new InvalidDataException("Eine benötigte Referenz fehlt oder zeigt auf einen falschen Unit-Typ.");
            if (!_parsed.TryGetValue(id, out var parsed))
            {
                var text = "SiiNunit\n{\n" + string.Join('\n', _lines, unit.StartLine, unit.EndLine - unit.StartLine + 1) + "\n}";
                parsed = ScsDefinitionDocument.Parse(text).Units.Single();
                _parsed.Add(id, parsed);
            }
            return parsed;
        }
        private static void ValidateBodyLine(string line)
        {
            var quoted = false;
            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];
                if (quoted && character == '\\') { index++; continue; }
                if (character == '"') { quoted = !quoted; continue; }
                if (quoted) continue;
                if (character == '#' || character == '/' && index + 1 < line.Length && line[index + 1] == '/') break;
                if (character is '{' or '}' or '@' || character == '/' && index + 1 < line.Length && line[index + 1] == '*')
                    throw new InvalidDataException("Nicht unterstützte verschachtelte SII-Struktur oder Direktive.");
            }
            if (quoted) throw new InvalidDataException("Mehrzeilige oder unvollständige SII-Zeichenfolge wird nicht unterstützt.");
        }
    }
}
