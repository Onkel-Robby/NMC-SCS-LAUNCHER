namespace NmcScsLauncher.Core;

public enum ScsPowertrainKind { Engine, Transmission }
public enum ScsPowertrainSourceKind { Save, Game, Dlc, LocalMod, Workshop }
public enum ScsPowertrainCompatibility
{
    ObservedInSave,
    ModelMatchUnverified,
    DifferentModel,
    Ambiguous,
    Unresolved
}

public sealed record ScsPowertrainSource(
    ScsPowertrainSourceKind Kind,
    string Name,
    string Location);

public sealed record ScsPowertrainModDirectory(GameType Game, string Name, string DirectoryPath);

/// <summary>
/// Discovery evidence only. A row is never a write authorization; the existing
/// save-derived selection and validated save transaction remain separate.
/// </summary>
public sealed record ScsDiscoveredPowertrain(
    GameType Game,
    ScsPowertrainKind Kind,
    string DataPath,
    string Name,
    string TruckModel,
    bool IsCurrent,
    ScsPowertrainCompatibility Compatibility,
    string CompatibilityReason,
    IReadOnlyList<ScsPowertrainSource> Sources);

public sealed record ScsPowertrainDiscoveryRequest(
    GameType Game,
    string? PreferredInstallPath,
    string? GameHomeDirectory,
    ScsActiveTruckPowertrainCatalog SaveCatalog,
    IReadOnlyList<ScsPowertrainModDirectory>? ModDirectories = null);

public sealed record ScsPowertrainDiscoveryResult(
    IReadOnlyList<ScsDiscoveredPowertrain> Components,
    IReadOnlyList<string> Diagnostics);

public interface IScsPowertrainDiscoveryService
{
    Task<ScsPowertrainDiscoveryResult> DiscoverAsync(
        ScsPowertrainDiscoveryRequest request,
        CancellationToken cancellationToken = default);
}
