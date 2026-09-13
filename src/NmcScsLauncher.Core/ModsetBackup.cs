namespace NmcScsLauncher.Core;

[Flags]
public enum ModsetBackupContent
{
    None = 0,
    Configuration = 1 << 0,
    Profiles = 1 << 1,
    Mods = 1 << 2
}

public sealed record ModsetBackupRequest(
    Guid ModsetId,
    string ArchivePath,
    ModsetBackupContent Content);

public sealed record ModsetBackupProgress(
    string Stage,
    long BytesProcessed,
    long TotalBytes,
    int FilesProcessed,
    int TotalFiles,
    string? CurrentItem = null)
{
    public double Percentage => TotalBytes > 0
        ? Math.Clamp(BytesProcessed * 100d / TotalBytes, 0d, 100d)
        : TotalFiles > 0
            ? Math.Clamp(FilesProcessed * 100d / TotalFiles, 0d, 100d)
            : Stage == "Completed" ? 100d : 0d;
}

public sealed record ModsetBackupManifestFile(
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteTimeUtc);

public sealed record ModsetBackupManifest(
    int SchemaVersion,
    string Product,
    string LauncherVersion,
    DateTimeOffset CreatedAtUtc,
    Guid ModsetId,
    string ModsetName,
    GameType Game,
    ModsetBackupContent Content,
    IReadOnlyList<ModsetBackupManifestFile> Files);

public sealed record ModsetBackupResult(
    string ArchivePath,
    int FilesArchived,
    long BytesArchived,
    ModsetBackupManifest Manifest,
    IReadOnlyList<string> Warnings);

public sealed record ModsetRestorePreview(
    string ArchivePath,
    ModsetBackupManifest Manifest,
    int FilesInArchive,
    long BytesInArchive,
    int ExistingTargetFiles,
    int NewTargetFiles);

public sealed record ModsetRestoreRequest(
    Guid TargetModsetId,
    string ArchivePath,
    bool OverwriteExisting);

public sealed record ModsetRestoreResult(
    int FilesRestored,
    long BytesRestored,
    int FilesOverwritten,
    ModsetBackupManifest Manifest);

public sealed class ModsetBackupValidationException : Exception
{
    public ModsetBackupValidationException(string message) : base(message)
    {
    }

    public ModsetBackupValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public interface IModsetBackupService
{
    Task<ModsetBackupResult> CreateAsync(
        ModsetBackupRequest request,
        IProgress<ModsetBackupProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IModsetRestoreService
{
    Task<ModsetRestorePreview> InspectAsync(
        Guid targetModsetId,
        string archivePath,
        CancellationToken cancellationToken = default);

    Task<ModsetRestoreResult> RestoreAsync(
        ModsetRestoreRequest request,
        IProgress<ModsetBackupProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
