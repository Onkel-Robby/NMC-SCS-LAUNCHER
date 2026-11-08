namespace NmcScsLauncher.Core;

public sealed record ScsTruckAccessorySnapshot(
    string UnitId, string UnitType, string DataPath, IReadOnlyList<string> UnsupportedReasons);

public enum ScsSaveUnitReadPhase { StructureValidation, FieldProjection }

/// <summary>Local diagnostic counts for LF-normalized unit text. Contains no unit ID or field values.</summary>
public sealed record ScsSaveUnitReadDiagnostic(string UnitType, int Characters, int Utf8Bytes, ScsSaveUnitReadPhase Phase);

public sealed record ScsTruckPowertrainSnapshot(
    GameType Game, string SaveFilePath, string SaveSha256, string TruckId, string TruckModelRoot,
    string TruckDataAccessoryId, string EngineAccessoryId, string TransmissionAccessoryId,
    string CurrentEnginePath, string CurrentTransmissionPath,
    IReadOnlyList<ScsTruckAccessorySnapshot> Accessories,
    IReadOnlyList<ScsSaveUnitReadDiagnostic>? UnitReadDiagnostics = null);

public sealed record ScsTruckPowertrainOption(
    ScsPowertrainKind Kind, string DataPath, string Name, bool IsCurrent,
    string SourceName, string TechnicalInfo, bool DefinitionVerified, IReadOnlyList<string> Reasons,
    IReadOnlyList<ScsPowertrainDiagnostic>? DiagnosticDetails = null);

public sealed record ScsTruckPowertrainAssessment(
    string EngineDataPath, string TransmissionDataPath, bool IsVerified, IReadOnlyList<string> Reasons,
    IReadOnlyList<ScsPowertrainDiagnostic>? DiagnosticDetails = null);

/// <summary>Detached read-only preview. It holds no open files and cannot authorize a later write.</summary>
public interface IScsTruckPowertrainPreview
{
    ScsTruckPowertrainSnapshot Snapshot { get; }
    IReadOnlyList<ScsTruckPowertrainOption> Engines { get; }
    IReadOnlyList<ScsTruckPowertrainOption> Transmissions { get; }
    IReadOnlyList<string> Diagnostics { get; }
    ScsTruckPowertrainInspectionReport? InspectionReport => null;
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
