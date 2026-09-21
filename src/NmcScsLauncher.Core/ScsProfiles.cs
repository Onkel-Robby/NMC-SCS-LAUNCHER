namespace NmcScsLauncher.Core;

public enum ScsProfileStorageKind
{
    Local = 1,
    SteamCloud = 2
}

public enum ScsSaveKind
{
    Manual = 1,
    AutoSave = 2,
    QuickSave = 3,
    Unknown = 4
}

public sealed record ScsProfileReference(
    GameType GameType,
    ScsProfileStorageKind StorageKind,
    string GameHomeDirectory,
    string ProfileDirectory,
    string ProfileDirectoryName,
    string DisplayName,
    string ProfileSiiPath);

public sealed record ScsSaveReference(
    GameType GameType,
    string ProfileDirectory,
    string SaveDirectory,
    string SaveDirectoryName,
    string DisplayName,
    ScsSaveKind Kind,
    string GameSiiPath,
    DateTimeOffset LastWriteTimeUtc);

public sealed record ScsCareerSkills(
    int AdrMask,
    int LongDistance,
    int HighValueCargo,
    int FragileCargo,
    int UrgentDelivery,
    int EcoDriving);

public interface IScsProfileSaveLocator
{
    Task<IReadOnlyList<ScsProfileReference>> FindProfilesAsync(
        GameType gameType,
        string gameHomeDirectory,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScsSaveReference>> FindSavesAsync(
        ScsProfileReference profile,
        CancellationToken cancellationToken = default);
}

public interface IScsProfileEditService
{
    Task<ScsSaveEditResult> RenameProfileAsync(
        ScsProfileReference profile,
        string newName,
        CancellationToken cancellationToken = default);

    Task<ScsSaveEditResult> SetMoneyAsync(
        ScsSaveReference save,
        long amount,
        CancellationToken cancellationToken = default);

    Task<ScsSaveEditResult> SetExperienceAsync(
        ScsSaveReference save,
        long experiencePoints,
        CancellationToken cancellationToken = default);

    Task<ScsCareerSkills> GetCareerSkillsAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default);

    Task<ScsSaveEditResult> SetCareerSkillsAsync(
        ScsSaveReference save,
        ScsCareerSkills skills,
        CancellationToken cancellationToken = default);
}
