namespace NmcScsLauncher.Core;

[Flags]
public enum ModsetCopyContent
{
    None = 0,
    Configuration = 1 << 0,
    Mods = 1 << 1,
    Profiles = 1 << 2,
    Screenshots = 1 << 3,
    Logs = 1 << 4
}

public sealed record ModsetDuplicationRequest(
    Guid SourceModsetId,
    string Name,
    string TargetHomeBasePath,
    ModsetCopyContent Content);

public sealed record ModsetDuplicationProgress(
    string Stage,
    long BytesCopied,
    long TotalBytes,
    int FilesCopied,
    int TotalFiles,
    string? CurrentItem = null)
{
    public double Percentage => TotalBytes > 0
        ? Math.Clamp(BytesCopied * 100d / TotalBytes, 0d, 100d)
        : TotalFiles > 0
            ? Math.Clamp(FilesCopied * 100d / TotalFiles, 0d, 100d)
            : Stage == "Completed" ? 100d : 0d;
}

public sealed record ModsetDuplicationResult(
    Modset Modset,
    int FilesCopied,
    long BytesCopied,
    IReadOnlyList<string> Warnings);

public interface IModsetDuplicationService
{
    Task<ModsetDuplicationResult> DuplicateAsync(
        ModsetDuplicationRequest request,
        IProgress<ModsetDuplicationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
