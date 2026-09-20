namespace NmcScsLauncher.Core;

public enum ScsProfileStorageKind
{
    Local,
    SteamCloud
}

public enum ScsSaveKind
{
    Manual,
    AutoSave,
    QuickSave,
    Unknown
}

public enum ScsSaveEditStatus
{
    Success,
    InvalidSelection,
    GameRunning,
    UnsupportedFormat,
    PropertyNotFound,
    AmbiguousProperty,
    NoChange,
    IoError
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

public sealed record ScsSaveBackupResult(
    string SourceDirectory,
    string BackupDirectory,
    DateTimeOffset CreatedAtUtc);

public sealed record ScsSaveEditResult(
    ScsSaveEditStatus Status,
    string Message,
    string? BackupDirectory = null)
{
    public bool Succeeded => Status == ScsSaveEditStatus.Success;
}

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

public interface IScsSaveBackupService
{
    Task<ScsSaveBackupResult> CreateAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default);
}

public interface IScsSaveTextCodec
{
    Task<string> ReadAsPlainTextAsync(
        string siiPath,
        CancellationToken cancellationToken = default);
}

public interface IScsGameProcessGuard
{
    bool IsGameRunning(GameType gameType);
}

public interface IScsSaveEditService
{
    Task<ScsSaveEditResult> SetMoneyAsync(
        ScsSaveReference save,
        long amount,
        CancellationToken cancellationToken = default);

    Task<ScsSaveEditResult> SetExperienceAsync(
        ScsSaveReference save,
        long experiencePoints,
        CancellationToken cancellationToken = default);
}
