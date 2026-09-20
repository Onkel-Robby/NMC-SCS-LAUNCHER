namespace NmcScsLauncher.Core;

public sealed record WorkshopContentItem(
    GameType Game,
    ulong PublishedFileId,
    string ContentPath,
    string SteamLibraryRoot,
    DateTimeOffset LastWriteTimeUtc,
    string? Title = null,
    DateTimeOffset? WorkshopUpdatedAtUtc = null)
{
    public bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    public string DisplayName => HasTitle
        ? Title!.Trim()
        : $"Workshop {PublishedFileId}";

    public DateTimeOffset DisplayLastUpdatedUtc => WorkshopUpdatedAtUtc ?? LastWriteTimeUtc;
}

public sealed record WorkshopPublishedFileDetails(
    ulong PublishedFileId,
    string Title,
    DateTimeOffset? UpdatedAtUtc);

public sealed record WorkshopContentSnapshot(
    GameType Game,
    IReadOnlyList<WorkshopContentItem> Items,
    IReadOnlyList<string> ScannedContentRoots,
    IReadOnlyList<string> Warnings)
{
    public int InstalledItemCount => Items.Count;
}

public interface IWorkshopContentLocator
{
    Task<WorkshopContentSnapshot> ScanAsync(
        GameType game,
        CancellationToken cancellationToken = default);

    Task<WorkshopContentSnapshot> ScanAsync(
        GameType game,
        string? preferredInstallPath,
        CancellationToken cancellationToken = default);
}

public interface IWorkshopMetadataProvider
{
    Task<IReadOnlyDictionary<ulong, WorkshopPublishedFileDetails>> GetDetailsAsync(
        GameType game,
        IEnumerable<ulong> publishedFileIds,
        CancellationToken cancellationToken = default);
}
