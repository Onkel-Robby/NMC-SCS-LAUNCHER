using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.Services;

/// <summary>Explicit export allowlist. Never serialize preview, request, evidence, messages or exceptions.</summary>
public static class TruckPowertrainDiagnosticReport
{
    public const int MaximumBytes = 256 * 1024;
    private const int MaximumItems = 128;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> Codes = new(StringComparer.Ordinal)
    {
        "active_package_missing", "profile_selection_invalid", "profile_metadata_invalid", "profile_save_mod_order_mismatch",
        "installation_unverified", "local_roots_ambiguous", "workshop_inventory_unverified", "workshop_package_missing",
        "workshop_package_ambiguous", "workshop_identity_invalid", "workshop_root_wrong_game", "workshop_versions_invalid",
        "workshop_version_missing", "workshop_version_ambiguous", "workshop_variant_informational", "package_missing",
        "package_ambiguous", "package_archive_unreadable", "package_unverified", "manifest_missing", "manifest_ambiguous",
        "manifest_invalid", "manifest_version_incompatible", "nested_package_unsupported", "dlc_availability_unverified",
        "dlc_metadata_invalid", "installation_archive_activation_unknown", "mod_dlc_dependency_unverified",
        "conditional_dlc_activation_unknown", "configuration_unverified", "definitions_not_attempted",
        "definition_unverified", "definition_read_failed", "definition_parse_failed", "definition_unavailable",
        "source_unreadable", "source_changed", "effective_content_unavailable", "accessory_unverified", "pair_unverified",
        "source_snapshot_invalid", "definition_priority_ambiguous", "inspection_unavailable", "accessory_verification_failed",
        "definition_availability_unverified", "definition_missing", "definition_invalid", "game_mismatch", "game_version_unsupported",
        "saved_accessory_unsupported", "accessory_limit", "diagnostic_limit", "source_listing_failed", "package_path_invalid"
    };

    public static string Create(ScsEffectiveContentRequest request, IScsTruckPowertrainPreview preview,
        ScsTruckPowertrainAssessment? selectedPair = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preview);
        var report = preview.InspectionReport;
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var privateNames = new[] { request.Profile.DisplayName, request.Profile.ProfileDirectoryName,
            request.Save.DisplayName, request.Save.SaveDirectoryName }.Where(value => value.Length >= 8).ToArray();
        bool Private(string value) => privateNames.Any(name => value.Contains(name, StringComparison.OrdinalIgnoreCase));
        bool Sensitive(string value) => Private(value) || Regex.IsMatch(value,
            @"(?i)(secret|password|token|license|credential|account|profile)|[0-9]{10,}|\b[a-f0-9]{12,}\b", RegexOptions.CultureInvariant);
        string? Label(string? value, bool identity = false)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || Private(value)) return null;
            // Only this explicit public Workshop namespace admits the long numeric/hex package identifier.
            if (identity && Regex.IsMatch(value, @"\Amod_workshop_package\.[a-fA-F0-9]{16}\z", RegexOptions.CultureInvariant)) return value;
            if (!Regex.IsMatch(value, @"\A[\p{L}\p{N} ._()+\-]+\z", RegexOptions.CultureInvariant) || Sensitive(value)) return null;
            return value;
        }
        string? Version(string? value) => value is { Length: <= 48 } &&
            Regex.IsMatch(value, @"\A[0-9]{1,4}(?:\.[0-9]{1,4}){0,4}s?\z", RegexOptions.CultureInvariant) ? value : null;
        string? VirtualPath(string? value) => value is { Length: <= 256 } && !Sensitive(value) &&
            value.StartsWith("/def/vehicle/truck/", StringComparison.Ordinal) &&
            Regex.IsMatch(value, @"\A/[a-zA-Z0-9_./\-]+\z", RegexOptions.CultureInvariant) &&
            !value.Split('/').Any(part => part is "." or "..") ? value : null;
        string? Alias(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            if (path.Length > 2048) return "<Source>/unclassified";
            if (aliases.TryGetValue(path, out var existing)) return existing;
            var root = "Source";
            if (Under(path, request.PreferredInstallPath)) root = "GameInstall";
            else
            {
                var roots = request.ModDirectories?.Take(MaximumItems).ToArray() ?? [];
                for (var index = 0; index < roots.Length; index++)
                    if (Under(path, roots[index].DirectoryPath)) { root = "ModRoot" + (index + 1); break; }
            }
            return aliases[path] = $"<{root}>/source-{aliases.Count + 1:000}";
        }
        object Issue(ScsPowertrainDiagnostic issue) => new
        {
            code = Codes.Contains(issue.Code) ? issue.Code : "unclassified",
            stage = EnumName(issue.Stage), scope = EnumName(issue.Scope), blocksConfiguration = issue.BlocksConfiguration,
            packageId = Label(issue.PackageIdentity, identity: true), packageName = Label(issue.DisplayName),
            packageVersion = Version(issue.PackageVersion), source = Alias(issue.PhysicalPath), virtualPath = VirtualPath(issue.VirtualPath)
        };
        object Option(ScsTruckPowertrainOption option) => new
        {
            kind = EnumName(option.Kind), virtualPath = VirtualPath(option.DataPath), current = option.IsCurrent,
            definitionVerified = option.DefinitionVerified,
            issues = option.DiagnosticDetails?.OrderBy(DiagnosticPriority).Take(8).Select(Issue).ToArray() ?? []
        };
        var mods = report?.ActiveMods ?? [];
        var sources = report?.Sources ?? [];
        var issues = report?.Issues ?? [];
        var dependencies = report?.SaveDependencies ?? [];
        object Dependency(ScsSaveDependency dependency)
        {
            var match = TruckPowertrainDependencyDiagnostics.Describe(dependency, report);
            return new
            {
                kind = EnumName(dependency.Kind), packageId = Label(dependency.Identity, identity: true),
                status = match.Status, matchingSourceCount = match.Sources.Count,
                matchingSources = match.Sources.Take(16).Select(source => Alias(source.PhysicalPath)).ToArray()
            };
        }
        var truncated = mods.Count > MaximumItems || sources.Count > MaximumItems || issues.Count > MaximumItems ||
            dependencies.Count > MaximumItems || dependencies.Take(MaximumItems).Any(dependency =>
                TruckPowertrainDependencyDiagnostics.Describe(dependency, report).Sources.Count > 16) ||
            preview.Engines.Count > MaximumItems || preview.Transmissions.Count > MaximumItems ||
            selectedPair?.DiagnosticDetails?.Count > MaximumItems;
        object Document(int limit, bool shortened) => new
        {
            schemaVersion = 1, appVersion = AppVersionInfo.Current, game = EnumName(request.Profile.GameType),
            gameVersion = Version(report?.GameVersion), configurationVerified = report?.ConfigurationVerified,
            definitionsAttempted = report?.DefinitionsAttempted, resultsInvalidated = report?.ResultsInvalidated,
            storageKind = EnumName(request.Profile.StorageKind),
            saveKind = EnumName(request.Save.Kind), truckModel = VirtualPath(preview.Snapshot.TruckModelRoot),
            counts = new { activeMods = mods.Count, sources = sources.Count, issues = issues.Count,
                engines = preview.Engines.Count, transmissions = preview.Transmissions.Count,
                saveDependencies = dependencies.Count,
                gameSources = sources.Count(source => source.Kind == ScsPowertrainSourceKind.Game),
                dlcSources = sources.Count(source => source.Kind == ScsPowertrainSourceKind.Dlc),
                localModSources = sources.Count(source => source.Kind == ScsPowertrainSourceKind.LocalMod),
                workshopSources = sources.Count(source => source.Kind == ScsPowertrainSourceKind.Workshop) },
            truncated = truncated || shortened,
            activeMods = mods.Take(limit).Select(mod => new { id = Label(mod.Identity, identity: true), name = Label(mod.DisplayName), loadOrder = mod.LoadOrder }).ToArray(),
            saveDependenciesRead = report?.SaveDependencies is not null,
            saveDependencies = dependencies.Take(limit).Select(Dependency).ToArray(),
            sources = sources.Take(limit).Select(source => new { source = Alias(source.PhysicalPath), kind = EnumName(source.Kind),
                packageId = Label(source.PackageIdentity, identity: true), packageVersion = Version(source.PackageVersion),
                priority = source.Priority, availabilityVerified = source.AvailabilityVerified,
                dlcMetadataVerified = source.DlcMetadataVerified,
                // Public manifest identity only, never ownership or activation evidence.
                dlcSteamAppId = source.Kind == ScsPowertrainSourceKind.Dlc && source.DlcMetadataVerified && source.DlcSteamAppId > 0
                    ? source.DlcSteamAppId : null }).ToArray(),
            issues = issues.OrderBy(DiagnosticPriority).Take(limit).Select(Issue).ToArray(),
            selectedPair = selectedPair is null ? null : new
            {
                engine = VirtualPath(selectedPair.EngineDataPath), transmission = VirtualPath(selectedPair.TransmissionDataPath),
                verified = selectedPair.IsVerified && report?.ResultsInvalidated != true,
                issueCount = selectedPair.DiagnosticDetails?.Count ?? 0, reasonCount = selectedPair.Reasons.Count,
                issues = selectedPair.DiagnosticDetails?.OrderBy(DiagnosticPriority).Take(limit).Select(Issue).ToArray() ?? []
            },
            engines = preview.Engines.Take(limit).Select(Option).ToArray(),
            transmissions = preview.Transmissions.Take(limit).Select(Option).ToArray()
        };
        var json = JsonSerializer.Serialize(Document(MaximumItems, false), JsonOptions);
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes)
        {
            aliases.Clear();
            json = JsonSerializer.Serialize(Document(16, true), JsonOptions);
        }
        if (Encoding.UTF8.GetByteCount(json) > MaximumBytes) throw new InvalidDataException("Diagnosebericht zu groß.");
        return json;
    }

    private static string EnumName<T>(T value) where T : struct, Enum => Enum.IsDefined(value) ? value.ToString() : "Unknown";

    internal static int DiagnosticPriority(ScsPowertrainDiagnostic issue) => issue.BlocksConfiguration ? 0 : issue.Scope switch
    {
        ScsPowertrainDiagnosticScope.Global => 1,
        ScsPowertrainDiagnosticScope.Component or ScsPowertrainDiagnosticScope.Pair => 2,
        _ => 3
    };

    private static bool Under(string path, string? root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalized.Equals(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException) { return false; }
    }
}
