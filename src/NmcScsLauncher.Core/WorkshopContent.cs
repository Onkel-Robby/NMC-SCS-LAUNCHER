namespace NmcScsLauncher.Core;

public sealed record WorkshopContentItem(
    GameType Game,
    ulong PublishedFileId,
    string ContentPath,
    string SteamLibraryRoot,
    DateTimeOffset LastWriteTimeUtc);

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
}
