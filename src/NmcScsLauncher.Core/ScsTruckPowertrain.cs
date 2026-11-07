namespace NmcScsLauncher.Core;

public sealed record ScsTruckAccessorySnapshot(
    string UnitId, string UnitType, string DataPath, IReadOnlyList<string> UnsupportedReasons);

public sealed record ScsTruckPowertrainSnapshot(
    GameType Game, string SaveFilePath, string SaveSha256, string TruckId, string TruckModelRoot,
    string TruckDataAccessoryId, string EngineAccessoryId, string TransmissionAccessoryId,
    string CurrentEnginePath, string CurrentTransmissionPath,
    IReadOnlyList<ScsTruckAccessorySnapshot> Accessories);

public sealed record ScsTruckPowertrainOption(
    ScsPowertrainKind Kind, string DataPath, string Name, bool IsCurrent,
    string SourceName, string TechnicalInfo, bool DefinitionVerified, IReadOnlyList<string> Reasons);

public sealed record ScsTruckPowertrainAssessment(
    string EngineDataPath, string TransmissionDataPath, bool IsVerified, IReadOnlyList<string> Reasons);

/// <summary>Detached read-only preview. It holds no open files and cannot authorize a later write.</summary>
public interface IScsTruckPowertrainPreview
{
    ScsTruckPowertrainSnapshot Snapshot { get; }
    IReadOnlyList<ScsTruckPowertrainOption> Engines { get; }
    IReadOnlyList<ScsTruckPowertrainOption> Transmissions { get; }
    IReadOnlyList<string> Diagnostics { get; }
    ScsTruckPowertrainAssessment AssessPair(string engineDataPath, string transmissionDataPath);
}

public interface IScsTruckPowertrainService
{
    Task<IScsTruckPowertrainPreview> InspectAsync(ScsEffectiveContentRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ScsTruckPowertrainInstallRequest(
    ScsEffectiveContentRequest Content,
    string ExpectedSaveSha256,
    string ExpectedTruckId,
    string EngineDataPath,
    string TransmissionDataPath);

public interface IScsTruckPowertrainInstaller
{
    Task<ScsSaveEditResult> InstallAsync(ScsTruckPowertrainInstallRequest request,
        CancellationToken cancellationToken = default);
}
