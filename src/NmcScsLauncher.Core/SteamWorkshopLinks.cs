namespace NmcScsLauncher.Core;

public static class SteamWorkshopLinks
{
    public static Uri GetItemUri(WorkshopContentItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return GetItemUri(item.PublishedFileId);
    }

    public static Uri GetItemUri(ulong publishedFileId)
    {
        if (publishedFileId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(publishedFileId), "PublishedFileId darf nicht 0 sein.");
        }

        return new Uri(
            $"https://steamcommunity.com/sharedfiles/filedetails/?id={publishedFileId}",
            UriKind.Absolute);
    }
}
