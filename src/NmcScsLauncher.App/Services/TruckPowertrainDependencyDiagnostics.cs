using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.Services;

/// <summary>Describes saved metadata without using it to authorize sources or writes.</summary>
internal static class TruckPowertrainDependencyDiagnostics
{
    internal sealed record Match(string Status, IReadOnlyList<ScsContentLayer> Sources);

    internal static Match Describe(ScsSaveDependency dependency, ScsTruckPowertrainInspectionReport? report)
    {
        if (dependency.Kind == ScsSaveDependencyKind.Mod)
            return new(report?.ActiveMods.Any(mod => string.Equals(mod.Identity, dependency.Identity,
                StringComparison.Ordinal)) == true ? "Active" : "Historical", []);

        if (dependency.Kind is not (ScsSaveDependencyKind.Dlc or ScsSaveDependencyKind.Rdlc))
            return new("Unknown", []);

        var invalidManifestPaths = (report?.Issues ?? [])
            .Where(issue => issue.Code == "dlc_metadata_invalid" && issue.PhysicalPath is not null)
            .Select(issue => issue.PhysicalPath!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sources = (report?.Sources ?? [])
            .Where(source => source.Kind == ScsPowertrainSourceKind.Dlc && source.DlcMetadataVerified &&
                string.Equals(source.PackageIdentity, dependency.Identity, StringComparison.Ordinal) &&
                !invalidManifestPaths.Contains(source.PhysicalPath))
            .DistinctBy(source => source.PhysicalPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new(sources.Length switch { 0 => "NoMatch", 1 => "Matched", _ => "Ambiguous" }, sources);
    }
}
