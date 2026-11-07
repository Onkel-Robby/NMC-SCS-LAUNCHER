using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

/// <summary>Builds detached evidence for a preview only. A later write must rebuild and revalidate it.</summary>
public static class ScsTruckPowertrainRules
{
    private const int MaximumDefinitions = 4096;
    private const int MaximumOptions = 1024;

    public static IScsTruckPowertrainPreview Build(ScsTruckPowertrainSnapshot snapshot,
        IScsEffectiveContentSession session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = new List<string>();
        // Evidence may describe unused DLCs. Only unresolved configuration or a referenced definition is a blocker.
        if (!session.Evidence.ConfigurationVerified)
            diagnostics.Add("Die aktive Spiel-, Profil- und Mod-Konfiguration ist nicht vollständig bestätigt.");
        if (session.Evidence.Game != snapshot.Game)
            diagnostics.Add("Spielstand und wirksame Inhalte gehören zu unterschiedlichen Spielen.");
        if (!Version.TryParse(session.Evidence.GameVersion, out var version) || version.Major != 1 || version.Minor is < 39 or > 62)
            diagnostics.Add("Die Spielversion liegt außerhalb des geprüften Definitionsschemas (1.39–1.62).");
        diagnostics.AddRange(snapshot.Accessories.SelectMany(accessory => accessory.UnsupportedReasons));
        if (snapshot.Accessories.Count > 512) diagnostics.Add("Der Truck überschreitet das Limit von 512 prüfbaren Zubehörinstanzen.");
        var loader = new Loader(session, cancellationToken);
        var definitions = new Dictionary<string, Definition>(StringComparer.Ordinal);
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        Definition? Load(string path)
        {
            if (definitions.TryGetValue(path, out var existing)) return existing;
            if (failures.ContainsKey(path)) return null;
            try
            {
                if (definitions.Count + failures.Count >= MaximumDefinitions)
                    throw new InvalidDataException("Zu viele Zubehördefinitionen für eine vollständige Prüfung.");
                var definition = loader.Load(path);
                definitions.Add(path, definition);
                return definition;
            }
            catch (Exception exception) when (IsDefinitionFailure(exception))
            {
                failures[path] = $"{path}: {exception.Message}";
                return null;
            }
        }

        var enginePaths = new HashSet<string>(StringComparer.Ordinal) { snapshot.CurrentEnginePath };
        var transmissionPaths = new HashSet<string>(StringComparer.Ordinal) { snapshot.CurrentTransmissionPath };
        if (session.Evidence.ConfigurationVerified && session.Evidence.Game == snapshot.Game && snapshot.Accessories.Count <= 512)
        {
            foreach (var accessory in snapshot.Accessories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = Load(accessory.DataPath);
                var retained = accessory.UnitId != snapshot.EngineAccessoryId && accessory.UnitId != snapshot.TransmissionAccessoryId;
                if (definition is null && retained) diagnostics.Add(failures[accessory.DataPath]);
                else if (definition is not null && retained && !MatchesSavedType(accessory.UnitType, definition.Unit.Type))
                    diagnostics.Add($"{accessory.DataPath}: Definitionstyp {definition.Unit.Type} passt nicht zur gespeicherten Zubehörklasse {accessory.UnitType}.");
            }
            if (definitions.TryGetValue(snapshot.Accessories.Single(a => a.UnitId == snapshot.TruckDataAccessoryId).DataPath, out var truck) &&
                truck.Unit.Type != "accessory_truck_data") diagnostics.Add("Die zentrale Truck-Definition ist nicht eindeutig bestätigt.");
            Enumerate("engine", enginePaths);
            Enumerate("transmission", transmissionPaths);
            foreach (var path in enginePaths.Concat(transmissionPaths)) Load(path);
            // Resolve every defaults/overrides target in advance. AssessPair is pure and holds no VFS handles.
            var visited = new HashSet<string>(definitions.Keys, StringComparer.Ordinal);
            var pending = new Queue<Definition>(definitions.Values);
            while (pending.TryDequeue(out var definition))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var target in definition.Defaults.Concat(definition.Overrides))
                {
                    var loaded = Load(target);
                    if (loaded is not null && visited.Add(target)) pending.Enqueue(loaded);
                }
            }
        }

        void Enumerate(string directory, HashSet<string> result)
        {
            try
            {
                var prefix = snapshot.TruckModelRoot + "/" + directory + "/";
                foreach (var path in session.GetFiles(snapshot.TruckModelRoot + "/" + directory, cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!path.StartsWith(prefix, StringComparison.Ordinal) || !path.EndsWith(".sii", StringComparison.Ordinal) ||
                        path[prefix.Length..].Contains('/')) continue;
                    if (result.Count >= MaximumOptions) throw new InvalidDataException("Zu viele Antriebsdefinitionen für eine vollständige Auswahl.");
                    result.Add(path);
                }
            }
            catch (Exception exception) when (IsDefinitionFailure(exception))
            { diagnostics.Add($"Die Liste {directory} konnte nicht vollständig gelesen werden: {exception.Message}"); }
        }

        return new Preview(snapshot, definitions, failures, enginePaths, transmissionPaths, diagnostics, session.Evidence.Diagnostics);
    }

    private static bool IsDefinitionFailure(Exception exception) => exception is IOException or InvalidDataException
        or UnauthorizedAccessException or ScsSaveEditException or FormatException or OverflowException;

    private static bool MatchesSavedType(string saved, string definition) => saved switch
    {
        "vehicle_accessory" => definition is "accessory_truck_data" or "accessory_engine_data" or "accessory_transmission_data"
            or "accessory_cabin_data" or "accessory_chassis_data" or "accessory_interior_data" or "accessory_head_lights_data",
        "vehicle_addon_accessory" or "vehicle_drv_plate_accessory" => definition is "accessory_addon_data" or "accessory_addon_int_data" or "accessory_addon_patch_data" or "accessory_addon_trailer_cables_data",
        "vehicle_wheel_accessory" => definition is "accessory_wheel_data" or "accessory_rim_data",
        "vehicle_paint_job_accessory" => definition == "accessory_paint_job_data",
        _ => false
    };

    private sealed class Preview : IScsTruckPowertrainPreview
    {
        private readonly IReadOnlyDictionary<string, Definition> _definitions;
        private readonly IReadOnlyDictionary<string, string> _failures;
        private readonly HashSet<string> _engines;
        private readonly HashSet<string> _transmissions;
        private readonly IReadOnlyList<string> _blockers;
        public Preview(ScsTruckPowertrainSnapshot snapshot, Dictionary<string, Definition> definitions,
            Dictionary<string, string> failures, HashSet<string> engines, HashSet<string> transmissions, List<string> diagnostics,
            IReadOnlyList<string> information)
        {
            Snapshot = snapshot;
            _definitions = definitions;
            _failures = failures;
            _engines = engines;
            _transmissions = transmissions;
            _blockers = Array.AsReadOnly(diagnostics.Distinct(StringComparer.Ordinal).ToArray());
            Diagnostics = Array.AsReadOnly(information.Concat(diagnostics).Distinct(StringComparer.Ordinal).ToArray());
            Engines = Options(engines, ScsPowertrainKind.Engine, snapshot.CurrentEnginePath);
            Transmissions = Options(transmissions, ScsPowertrainKind.Transmission, snapshot.CurrentTransmissionPath);
        }
        public ScsTruckPowertrainSnapshot Snapshot { get; }
        public IReadOnlyList<ScsTruckPowertrainOption> Engines { get; }
        public IReadOnlyList<ScsTruckPowertrainOption> Transmissions { get; }
        public IReadOnlyList<string> Diagnostics { get; }

        private IReadOnlyList<ScsTruckPowertrainOption> Options(IEnumerable<string> paths, ScsPowertrainKind kind, string current) =>
            Array.AsReadOnly(paths.OrderBy(path => path == current ? 0 : 1).ThenBy(path => path, StringComparer.Ordinal).Select(path =>
            {
                _definitions.TryGetValue(path, out var definition);
                var reasons = new List<string>();
                if (definition is null) reasons.Add(_failures.GetValueOrDefault(path) ?? "Die wirksame Definition ist nicht bestätigt.");
                else if (definition.Unit.Type != ExpectedType(kind)) reasons.Add("Die Definition hat nicht den erwarteten Zubehörtyp.");
                return new ScsTruckPowertrainOption(kind, path, definition?.Name ?? FallbackName(path), path == current,
                    definition?.SourceName ?? "Quelle nicht bestätigt", definition?.TechnicalInfo ?? "Technische Angaben nicht bestätigt",
                    reasons.Count == 0, Array.AsReadOnly(reasons.ToArray()));
            }).ToArray());

        public ScsTruckPowertrainAssessment AssessPair(string engineDataPath, string transmissionDataPath)
        {
            var reasons = new List<string>(_blockers);
            if (!_engines.Contains(engineDataPath) || !_transmissions.Contains(transmissionDataPath))
                reasons.Add("Die Auswahl gehört nicht zur geprüften Liste dieses Trucks.");
            if (reasons.Count > 0) return new(engineDataPath, transmissionDataPath, false, Array.AsReadOnly(reasons.ToArray()));
            var selected = new List<Definition>();
            foreach (var accessory in Snapshot.Accessories)
            {
                var path = accessory.UnitId == Snapshot.EngineAccessoryId ? engineDataPath :
                    accessory.UnitId == Snapshot.TransmissionAccessoryId ? transmissionDataPath : accessory.DataPath;
                if (!_definitions.TryGetValue(path, out var definition))
                { reasons.Add(_failures.GetValueOrDefault(path) ?? $"{path}: Definition nicht bestätigt."); continue; }
                if ((accessory.UnitId == Snapshot.EngineAccessoryId && definition.Unit.Type != ExpectedType(ScsPowertrainKind.Engine)) ||
                    (accessory.UnitId == Snapshot.TransmissionAccessoryId && definition.Unit.Type != ExpectedType(ScsPowertrainKind.Transmission)))
                    reasons.Add($"{path}: Falscher Antriebstyp.");
                selected.Add(definition);
            }
            if (!_definitions.TryGetValue(Snapshot.CurrentEnginePath, out var currentEngine) || currentEngine.EngineType != "diesel" ||
                !_definitions.TryGetValue(engineDataPath, out var engine) || engine.EngineType != "diesel")
                reasons.Add("Nur ein bestätigter Diesel-zu-Diesel-Wechsel wird unterstützt; andere Motortypen benötigen zusätzliche Zustandsänderungen.");
            foreach (var group in selected.GroupBy(d => d.Unit.Id, StringComparer.Ordinal))
                if (group.Select(d => d.Path).Distinct(StringComparer.Ordinal).Skip(1).Any())
                    reasons.Add($"Zubehör-Unit {group.Key} ist durch unterschiedliche Definitionen mehrdeutig.");
            if (selected.Sum(d => d.Suitable.Count + d.Conflicts.Count + d.Requires.Count + d.Defaults.Count + d.Overrides.Count) > 2048)
                return new(engineDataPath, transmissionDataPath, false, new[] { "Die Konfiguration überschreitet das Limit von 2048 Zubehörregeln." });
            foreach (var definition in selected)
            {
                // Alternatives within one explicit accessory type are OR, different types remain AND.
                // SCS Max: https://forum.scssoft.com/viewtopic.php?p=338512 (all suitable engines in one sound list).
                // Also confirmed by mutually exclusive cabin alternatives in original Volvo FH16 definitions.
                foreach (var group in definition.Suitable.GroupBy(SuitableCategory, StringComparer.Ordinal))
                    if (!group.Any(pattern => selected.Any(other => WildcardMatches(pattern, other.Unit.Id))))
                        reasons.Add($"{definition.Name}: Keine suitable_for-Alternative für '{group.Key ?? group.First()}' passt zur gewählten Konfiguration.");
                foreach (var pattern in definition.Conflicts)
                    if (selected.Any(other => WildcardMatches(pattern, other.Unit.Id)))
                        reasons.Add($"{definition.Name}: conflict_with '{pattern}' widerspricht der gewählten Konfiguration.");
                foreach (var required in definition.Requires)
                    if (!selected.Any(other => other.AccessoryType == required))
                        reasons.Add($"{definition.Name}: Der erforderliche Zubehörtyp '{required}' fehlt.");
                foreach (var target in definition.Defaults.Concat(definition.Overrides))
                {
                    if (!_definitions.TryGetValue(target, out var dependency))
                    { reasons.Add(_failures.GetValueOrDefault(target) ?? $"{target}: Abhängige Definition nicht bestätigt."); continue; }
                    if (definition.Overrides.Contains(target, StringComparer.Ordinal))
                    {
                        if (!selected.Any(other => other.Path == target && other.Unit.Id == dependency.Unit.Id) ||
                            selected.Any(other => other.AccessoryType == dependency.AccessoryType && other.Path != target))
                            reasons.Add($"{definition.Name}: overrides würde weiteres Zubehör ändern ({target}).");
                    }
                    else if (!selected.Any(other => other.AccessoryType == dependency.AccessoryType))
                        reasons.Add($"{definition.Name}: defaults würde fehlendes Zubehör ergänzen ({target}).");
                }
            }
            var unique = Array.AsReadOnly(reasons.Distinct(StringComparer.Ordinal).ToArray());
            return new(engineDataPath, transmissionDataPath, unique.Count == 0, unique);
        }
    }

    private sealed record Definition(string Path, ScsDefinitionUnit Unit, string Name, string SourceName,
        string TechnicalInfo, string? EngineType, IReadOnlyList<string> Suitable, IReadOnlyList<string> Conflicts,
        IReadOnlyList<string> Requires, IReadOnlyList<string> Defaults, IReadOnlyList<string> Overrides)
    { public string AccessoryType => Unit.Id[(Unit.Id.LastIndexOf('.') + 1)..]; }

    private sealed class Loader(IScsEffectiveContentSession session, CancellationToken cancellationToken)
    {
        private long _totalCharacters;
        private int _readCount;
        public Definition Load(string path)
        {
            var sources = new List<string>();
            var expanded = Expand(Normalize(path, "/"), new HashSet<string>(StringComparer.Ordinal), sources, 0);
            var document = ScsDefinitionDocument.Parse(expanded);
            var accessories = document.Units.Where(unit => unit.Type.StartsWith("accessory_", StringComparison.Ordinal)).ToArray();
            if (accessories.Length != 1) throw new InvalidDataException("Die Datei enthält nicht genau eine Zubehördefinition.");
            var unit = accessories[0];
            if (unit.Id.Length > 256) throw new InvalidDataException("Die Zubehör-Unit überschreitet die sichere Namenslänge von 256 Zeichen.");
            ScsTruckDefinitionSchema.Validate(document, unit, session.Evidence.GameVersion);
            var suitable = Patterns(unit.GetStrings("suitable_for"));
            if (suitable.Count > 1 && suitable.Any(pattern => SuitableCategory(pattern) is null))
                throw new InvalidDataException("suitable_for enthält mehrdeutige Zubehörtypen; die Gruppierung wird nicht geraten.");
            var conflicts = Patterns(unit.GetStrings("conflict_with"));
            var required = unit.GetTokens("require");
            if (required.Any(value => !Regex.IsMatch(value, "^[A-Za-z0-9_]{1,12}$", RegexOptions.CultureInvariant)))
                throw new InvalidDataException("require enthält keinen eindeutigen Zubehörtyp.");
            var defaults = unit.GetStrings("defaults").Select(value => Normalize(value, path)).ToArray();
            var overrides = unit.GetStrings("overrides").Select(value => Normalize(value, path)).ToArray();
            var rawName = unit.GetString("name");
            var name = string.IsNullOrWhiteSpace(rawName) || rawName.Contains("@@", StringComparison.Ordinal)
                ? FallbackName(path) : rawName.Trim();
            return new(path, unit, name, string.Join(" + ", sources.Distinct(StringComparer.Ordinal)),
                TechnicalInfo(unit), unit.Type == "accessory_engine_data" ? unit.GetToken("type") ?? "diesel" : null,
                suitable, conflicts, required, Array.AsReadOnly(defaults), Array.AsReadOnly(overrides));
        }

        private string Expand(string path, HashSet<string> stack, List<string> sources, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > 16 || !stack.Add(path)) throw new InvalidDataException("Include-Zyklus oder zu tiefe Include-Kette.");
            if (++_readCount > 16384) throw new InvalidDataException("Zu viele Definitions- und Include-Zugriffe.");
            try
            {
                var definition = session.ReadDefinition(path, cancellationToken);
                if (!definition.AvailabilityVerified) throw new InvalidDataException("Verfügbarkeit nicht bestätigt: " + definition.AvailabilityReason);
                _totalCharacters += definition.Content.Length;
                if (_totalCharacters > 32 * 1024 * 1024) throw new InvalidDataException("Definitionskatalog überschreitet die sichere Lesegrenze.");
                sources.Add(definition.Source.PackageIdentity);
                var content = StripComments(definition.Content);
                var result = new StringBuilder();
                foreach (var line in content.Split('\n'))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("@include", StringComparison.Ordinal))
                    {
                        var match = Regex.Match(trimmed, "^@include[ \\t]+\"([^\"\\\\]+)\"[ \\t]*$", RegexOptions.CultureInvariant);
                        if (!match.Success) throw new InvalidDataException("Nicht unterstützte Include-Syntax.");
                        result.AppendLine(Expand(Normalize(match.Groups[1].Value, path), stack, sources, depth + 1));
                    }
                    else result.AppendLine(line);
                    if (result.Length > 8 * 1024 * 1024) throw new InvalidDataException("Expandierte Definition überschreitet die sichere Größe.");
                }
                return result.ToString();
            }
            finally { stack.Remove(path); }
        }
    }

    internal static string Normalize(string value, string includingPath)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Contains('\\') || value.Contains(':') ||
            value.Any(char.IsControl)) throw new InvalidDataException("Ungültiger virtueller Definitionspfad.");
        var combined = value.StartsWith('/') ? value : includingPath[..(includingPath.LastIndexOf('/') + 1)] + value;
        var segments = new List<string>();
        foreach (var segment in combined.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) throw new InvalidDataException("Include verlässt den virtuellen Wurzelpfad.");
                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                if (!Regex.IsMatch(segment, "^[A-Za-z0-9_.-]+$", RegexOptions.CultureInvariant))
                    throw new InvalidDataException("Nicht unterstützter virtueller Dateiname.");
                segments.Add(segment);
            }
        }
        var path = "/" + string.Join('/', segments);
        if (!path.StartsWith("/def/", StringComparison.Ordinal) || !(path.EndsWith(".sii", StringComparison.Ordinal) || path.EndsWith(".sui", StringComparison.Ordinal)))
            throw new InvalidDataException("Die Referenz ist keine unterstützte SII/SUI-Definition unter /def.");
        return path;
    }

    private static string StripComments(string text)
    {
        var result = new StringBuilder(text.Length);
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                result.Append(c);
                if (c == '\\' && i + 1 < text.Length) result.Append(text[++i]);
                else if (c == '"') quoted = false;
            }
            else if (c == '"') { quoted = true; result.Append(c); }
            else if (c == '#' || (c == '/' && i + 1 < text.Length && text[i + 1] == '/'))
            {
                while (i < text.Length && text[i] != '\n') { result.Append(' '); i++; }
                if (i < text.Length) result.Append('\n');
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                result.Append("  "); i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/'))
                { result.Append(text[i] == '\n' ? '\n' : ' '); i++; }
                if (i + 1 >= text.Length) throw new InvalidDataException("Nicht abgeschlossener SII-Kommentar.");
                result.Append("  "); i++;
            }
            else result.Append(c);
        }
        return result.ToString();
    }

    private static IReadOnlyList<string> Patterns(IReadOnlyList<string> values)
    {
        if (values.Count > 256 || values.Any(value => value.Length > 256 ||
            !Regex.IsMatch(value, "^[A-Za-z0-9_.*]+$", RegexOptions.CultureInvariant)))
            throw new InvalidDataException("Nicht unterstütztes Zubehör-Pattern.");
        return values;
    }

    private static bool WildcardMatches(string pattern, string value)
    {
        var p = 0; var v = 0; var star = -1; var retry = 0;
        while (v < value.Length)
        {
            if (p < pattern.Length && pattern[p] == value[v]) { p++; v++; }
            else if (p < pattern.Length && pattern[p] == '*') { star = p++; retry = v; }
            else if (star >= 0) { p = star + 1; v = ++retry; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    private static string? SuitableCategory(string pattern)
    {
        var separator = pattern.LastIndexOf('.');
        if (separator < 0) return null;
        var suffix = pattern[(separator + 1)..];
        return Regex.IsMatch(suffix, "^[A-Za-z0-9_]{1,12}$", RegexOptions.CultureInvariant) ? suffix : null;
    }

    private static string ExpectedType(ScsPowertrainKind kind) => kind == ScsPowertrainKind.Engine ? "accessory_engine_data" : "accessory_transmission_data";
    private static string FallbackName(string path) => Path.GetFileNameWithoutExtension(path).Replace('_', ' ').Replace('-', ' ');
    private static string TechnicalInfo(ScsDefinitionUnit unit)
    {
        if (unit.Type == "accessory_engine_data")
        {
            var result = ScsTruckDefinitionSchema.Number(unit.GetToken("torque") ?? "0").ToString("0.##", CultureInfo.InvariantCulture) + " Nm · " + (unit.GetToken("type") ?? "diesel");
            // SCS info[] is UI metadata, not a simulated power measurement. Never reinterpret hp as metric PS.
            var rating = unit.GetStrings("info").FirstOrDefault();
            if (rating is not null && rating.Length <= 1024)
            {
                var matches = Regex.Matches(rating, @"(?<![\d.,+\-])(?<kw>\d+(?:[.,]\d{1,2})?)\s*(?:kW|@@kw@@)(?![A-Za-z])", RegexOptions.CultureInvariant);
                if (matches.Count == 1 && double.TryParse(matches[0].Groups["kw"].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var kilowatts) &&
                    double.IsFinite(kilowatts) && kilowatts > 0)
                    result += " · laut Definition: " + kilowatts.ToString("0.##", CultureInfo.InvariantCulture) + " kW ≈ " +
                        (kilowatts / 0.73549875).ToString("0", CultureInfo.InvariantCulture) + " PS";
            }
            return result;
        }
        if (unit.Type == "accessory_transmission_data")
            return $"{unit.GetTokens("ratios_forward").Count} Vorwärtsgänge · {unit.GetTokens("ratios_reverse").Count} Rückwärtsgänge · Achsübersetzung " +
                ScsTruckDefinitionSchema.Number(unit.GetToken("differential_ratio") ?? "1").ToString("0.###", CultureInfo.InvariantCulture) +
                $" · Retarder: {unit.GetToken("retarder") ?? "0"}";
        return string.Empty;
    }
}
