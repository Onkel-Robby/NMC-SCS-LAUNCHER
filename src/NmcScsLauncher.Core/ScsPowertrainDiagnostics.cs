namespace NmcScsLauncher.Core;

public enum ScsPowertrainDiagnosticStage
{
    ArchiveDiscovery, ActiveConfiguration, EffectiveDefinition, TruckModel,
    AccessoryRules, PairCompatibility, Installation
}

public enum ScsPowertrainDiagnosticScope { Global, Source, Component, Pair }

/// <summary>Local diagnostic context. Exporters must select and redact fields, never serialize it wholesale.</summary>
public sealed record ScsPowertrainDiagnostic(
    string Code,
    ScsPowertrainDiagnosticStage Stage,
    ScsPowertrainDiagnosticScope Scope,
    string Message,
    string? PackageIdentity = null,
    string? DisplayName = null,
    string? PhysicalPath = null,
    string? PackageVersion = null,
    string? VirtualPath = null,
    string? TechnicalDetails = null,
    bool BlocksConfiguration = false);

public sealed record ScsTruckPowertrainInspectionReport(
    string GameVersion,
    bool ConfigurationVerified,
    bool DefinitionsAttempted,
    IReadOnlyList<ScsActiveMod> ActiveMods,
    IReadOnlyList<ScsContentLayer> Sources,
    IReadOnlyList<ScsPowertrainDiagnostic> Issues,
    bool ResultsInvalidated = false,
    IReadOnlyList<ScsSaveDependency>? SaveDependencies = null);

public sealed class ScsPowertrainDiagnosticException : ScsSaveEditException
{
    public ScsPowertrainDiagnosticException(ScsPowertrainDiagnostic diagnostic, Exception? innerException = null,
        ScsActiveContentEvidence? evidence = null)
        : base(diagnostic.Message, innerException)
    {
        Diagnostic = diagnostic;
        Evidence = evidence;
    }

    public ScsPowertrainDiagnostic Diagnostic { get; }
    public ScsActiveContentEvidence? Evidence { get; }
}
