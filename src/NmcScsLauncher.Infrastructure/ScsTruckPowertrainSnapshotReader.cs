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
    private static readonly Regex UnitHeader = new(@"^(?<type>[A-Za-z_][A-Za-z0-9_]{0,127})[ \t]*:[ \t]*(?<id>[A-Za-z0-9_.]+)[ \t]*\{$", RegexOptions.Compiled);
    private static readonly Regex Reference = new(@"^\.?[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*$", RegexOptions.Compiled);
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
            return new(Parse(save, sha, decoded.Content, cancellationToken), decoded.Content);
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

    public static ScsTruckPowertrainSnapshot Parse(ScsSaveReference save, string sha256, string content,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(content);
        var path = ValidateSavePath(save);
        if (sha256 is null || sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ScsSaveEditException("Der Save-Snapshot benötigt einen gültigen SHA-256-Fingerabdruck.");
        ValidateTextSize(content);
        try
        {
            var index = new SaveIndex(content, cancellationToken);
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
                Array.AsReadOnly(accessories), Array.AsReadOnly(index.Diagnostics.ToArray()));
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
        var withoutWindowsNewlines = read.Content.Replace("\r\n", string.Empty, StringComparison.Ordinal);
        if (withoutWindowsNewlines.Contains('\r') || read.Content.Contains("\r\n", StringComparison.Ordinal) && withoutWindowsNewlines.Contains('\n'))
            throw new ScsSaveEditException("Der Save enthält gemischte Zeilenenden; eine Änderung ohne weitere Textanpassungen ist nicht bestätigt.");
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
        var path = ReadField(unit, "data_path", () => unit.GetString("data_path")) ?? throw new InvalidDataException("Eine Zubehör-Unit hat keinen eindeutigen data_path.");
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
            var slots = ReadField(unit, "slot_name", () => unit.GetValues("slot_name"));
            var hookups = ReadField(unit, "slot_hookup", () => unit.GetValues("slot_hookup"));
            if (slots.Count != hookups.Count || slots.Count > 0 || hookups.Count > 0)
                reasons.Add("Nicht leere Slot-/Hookup-Zuordnungen können noch nicht vollständig geprüft werden.");
            if (ReadField(unit, "slot_name", () => unit.GetDeclaredArrayCount("slot_name")) is null ||
                ReadField(unit, "slot_hookup", () => unit.GetDeclaredArrayCount("slot_hookup")) is null)
                reasons.Add("Slot-/Hookup-Zuordnungen haben keine eindeutige deklarierte Anzahl.");
        }
        return new(unit.Id, unit.Type, path, reasons.AsReadOnly());
    }

    private static IReadOnlyList<string> ReadReferences(ScsDefinitionUnit unit, string field)
    {
        if (ReadField(unit, field, () => unit.GetDeclaredArrayCount(field)) is not { } count || count > 10000)
            throw new InvalidDataException($"Referenzliste '{field}' benötigt eine begrenzte deklarierte Anzahl.");
        var references = ReadField(unit, field, () => unit.GetTokens(field));
        if (references.Count != count || references.Any(value => !IsReference(value)) || references.Distinct(StringComparer.Ordinal).Count() != count)
            throw new InvalidDataException($"Referenzliste '{field}' ist unvollständig, doppelt oder ungültig.");
        return references;
    }

    private static string RequiredReference(ScsDefinitionUnit unit, string field) => OptionalReference(unit, field) ??
        throw new InvalidDataException($"Die aktive Referenz '{field}' fehlt.");
    private static string? OptionalReference(ScsDefinitionUnit unit, string field)
    {
        var token = ReadField(unit, field, () => unit.GetToken(field));
        if (token is null or "null") return null;
        if (!IsReference(token)) throw new InvalidDataException($"Referenz '{field}' ist ungültig.");
        return token;
    }
    private static bool IsReference(string value) => value != "null" && value.Length <= 256 && Reference.IsMatch(value);
    private static T ReadField<T>(ScsDefinitionUnit unit, string field, Func<T> read)
    {
        try { return read(); }
        catch (InvalidDataException)
        {
            // Typed metadata errors include unit IDs by default. The save diagnostic must not.
            // field is always a fixed schema name from this reader, never a value from the save.
            throw new InvalidDataException($"Ungültiger Typ für benötigtes Feld '{field}' (Unit-Typ {unit.Type}, Prüfphase Truck-Zuordnung).");
        }
    }
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
    /// Shares the save parser's immutable lines. Every unit and field is validated; only
    /// bounded projections of required fields reach the separate metadata AST parser.
    /// </summary>
    private sealed class SaveIndex
    {
        private readonly ScsSiiUnitDocument _document;
        private readonly CancellationToken _cancellation;
        private readonly Dictionary<string, IndexedUnit> _units = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ScsDefinitionUnit> _parsed = new(StringComparer.Ordinal);
        internal List<ScsSiiUnit> AccessoryOwners { get; } = [];
        internal List<ScsSaveUnitReadDiagnostic> Diagnostics { get; } = [];

        internal SaveIndex(string content, CancellationToken cancellation)
        {
            _cancellation = cancellation;
            _document = ScsSiiUnitDocument.Parse(content);
            var lines = _document.Lines;
            var state = 0;
            ScsSiiUnit? current = null;
            for (var number = 0; number < lines.Length; number++)
            {
                if ((number & 1023) == 0) cancellation.ThrowIfCancellationRequested();
                var line = lines[number].AsSpan().Trim(" \t\r".AsSpan());
                if (number == 0) line = line.TrimStart('\uFEFF');
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("//", StringComparison.Ordinal)) continue;
                if (state == 0 && line is "SiiNunit" or "SiiNunit {") { state = line.EndsWith("{", StringComparison.Ordinal) ? 2 : 1; continue; }
                if (state == 1 && line is "{") { state = 2; continue; }
                if (state == 2)
                {
                    if (line is "}") { state = 4; continue; }
                    if (line.Length > 512) throw new InvalidDataException("SII-Unit-Deklaration überschreitet das Leselimit.");
                    var match = UnitHeader.Match(line.ToString());
                    if (!match.Success || !IsReference(match.Groups["id"].Value)) throw new InvalidDataException("Nicht unterstützte oder uneindeutige SII-Unit-Deklaration.");
                    current = new(match.Groups["type"].Value, match.Groups["id"].Value, number, -1);
                    state = 3;
                    continue;
                }
                if (state == 3)
                {
                    if (line is "}")
                    {
                        current = current! with { EndLine = number };
                        var indexed = Measure(current);
                        if (_units.Count >= 500000 || !_units.TryAdd(current.Id, indexed)) throw new InvalidDataException("Doppelte SII-Unit oder Unit-Leselimit überschritten.");
                        if (Project(indexed, capture: false).HasAccessories) AccessoryOwners.Add(current);
                        state = 2;
                    }
                    continue;
                }
                throw new InvalidDataException("Nicht unterstützte SII-Dokumentstruktur.");
            }
            if (state != 4) throw new InvalidDataException("Unvollständiges SII-Dokument.");
        }

        internal ScsDefinitionUnit SingleType(string type)
        {
            var matches = _units.Values.Where(unit => unit.Unit.Type == type).Take(2).ToArray();
            if (matches.Length != 1) throw new InvalidDataException($"Unit-Typ '{type}' ist nicht eindeutig.");
            return Required(matches[0].Unit.Id, type);
        }
        internal ScsDefinitionUnit Required(string id, string? type = null)
        {
            if (!_units.TryGetValue(id, out var unit) || type is not null && unit.Unit.Type != type)
                throw new InvalidDataException("Eine benötigte Referenz fehlt oder zeigt auf einen falschen Unit-Typ.");
            if (!_parsed.TryGetValue(id, out var parsed))
            {
                var projection = Project(unit, capture: true);
                try { parsed = projection.ToDefinition(); }
                catch (InvalidDataException exception)
                { throw UnitFailure(unit, ScsSaveUnitReadPhase.FieldProjection, exception.Message); }
                _parsed.Add(id, parsed);
            }
            return parsed;
        }
        private IndexedUnit Measure(ScsSiiUnit unit)
        {
            var characters = unit.EndLine - unit.StartLine;
            var bytes = characters; // One LF between the original unit lines.
            for (var number = unit.StartLine; number <= unit.EndLine; number++)
            {
                if ((number & 1023) == 0) _cancellation.ThrowIfCancellationRequested();
                characters = checked(characters + _document.Lines[number].Length);
                bytes = checked(bytes + Encoding.UTF8.GetByteCount(_document.Lines[number]));
            }
            return new(unit, characters, bytes);
        }

        private ScsSaveUnitProjection Project(IndexedUnit indexed, bool capture)
        {
            var phase = capture ? ScsSaveUnitReadPhase.FieldProjection : ScsSaveUnitReadPhase.StructureValidation;
            var unit = indexed.Unit;
            try
            {
                var projection = new ScsSaveUnitProjection(unit, capture);
                for (var number = unit.StartLine + 1; number < unit.EndLine; number++)
                {
                    if ((number & 1023) == 0) _cancellation.ThrowIfCancellationRequested();
                    projection.AddLine(_document.Lines[number]);
                }
                projection.Complete();
                if (indexed.Characters > 8 * 1024 * 1024 && Diagnostics.Count < 32)
                    Diagnostics.Add(new(unit.Type, indexed.Characters, indexed.Utf8Bytes, phase));
                return projection;
            }
            catch (InvalidDataException exception)
            {
                throw UnitFailure(indexed, phase, exception.Message);
            }
        }

        private static InvalidDataException UnitFailure(IndexedUnit unit, ScsSaveUnitReadPhase phase, string reason) =>
            new($"Unit-Typ {unit.Unit.Type} · {unit.Characters} Zeichen / {unit.Utf8Bytes} UTF-8-Bytes (LF) · Prüfphase {phase}: {reason}");

        private sealed record IndexedUnit(ScsSiiUnit Unit, int Characters, int Utf8Bytes);
    }
}
