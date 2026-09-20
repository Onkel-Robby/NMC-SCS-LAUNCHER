using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class SteamWorkshopContentLocator : IWorkshopContentLocator
{
    private readonly SteamLibraryLocator _libraryLocator;

    public SteamWorkshopContentLocator(SteamLibraryLocator libraryLocator)
    {
        _libraryLocator = libraryLocator ?? throw new ArgumentNullException(nameof(libraryLocator));
    }

    public Task<WorkshopContentSnapshot> ScanAsync(
        GameType game,
        CancellationToken cancellationToken = default) =>
        ScanAsync(game, preferredInstallPath: null, cancellationToken);

    public Task<WorkshopContentSnapshot> ScanAsync(
        GameType game,
        string? preferredInstallPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var definition = GameDefinition.For(game);
        var items = new List<WorkshopContentItem>();
        var scannedRoots = new List<string>();
        var warnings = new List<string>();

        var libraryRoots = new HashSet<string>(
            _libraryLocator.FindLibraryRoots(),
            StringComparer.OrdinalIgnoreCase);

        var inferredLibraryRoot = TryInferSteamLibraryRoot(preferredInstallPath);
        if (inferredLibraryRoot is not null)
        {
            libraryRoots.Add(inferredLibraryRoot);
        }

        foreach (var libraryRoot in libraryRoots.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contentRoot = Path.Combine(
                libraryRoot,
                "steamapps",
                "workshop",
                "content",
                definition.SteamAppId.ToString());

            if (!Directory.Exists(contentRoot))
            {
                continue;
            }

            scannedRoots.Add(contentRoot);
            string[] itemDirectories;
            try
            {
                itemDirectories = Directory.GetDirectories(contentRoot, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"Workshop-Inhalte konnten nicht gelesen werden: {contentRoot}");
                continue;
            }

            foreach (var itemDirectory in itemDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var directoryName = Path.GetFileName(itemDirectory);
                if (!ulong.TryParse(directoryName, out var publishedFileId))
                {
                    continue;
                }

                try
                {
                    if (File.GetAttributes(itemDirectory).HasFlag(FileAttributes.ReparsePoint))
                    {
                        warnings.Add($"Workshop-Verzeichnis {publishedFileId} wurde übersprungen, weil es ein Link/Junction ist.");
                        continue;
                    }

                    var info = new DirectoryInfo(itemDirectory);
                    items.Add(new WorkshopContentItem(
                        game,
                        publishedFileId,
                        info.FullName,
                        Path.GetFullPath(libraryRoot),
                        new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Workshop-Verzeichnis {publishedFileId} konnte nicht geprüft werden.");
                }
            }
        }

        var orderedItems = items
            .GroupBy(static item => item.PublishedFileId)
            .Select(static group => group
                .OrderByDescending(static item => item.LastWriteTimeUtc)
                .First())
            .OrderBy(static item => item.PublishedFileId)
            .ToArray();

        return Task.FromResult(new WorkshopContentSnapshot(
            game,
            orderedItems,
            scannedRoots.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings));
    }

    private static string? TryInferSteamLibraryRoot(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        try
        {
            var installDirectory = new DirectoryInfo(Path.GetFullPath(installPath.Trim()));
            var commonDirectory = installDirectory.Parent;
            var steamAppsDirectory = commonDirectory?.Parent;
            var libraryDirectory = steamAppsDirectory?.Parent;

            if (commonDirectory is null
                || steamAppsDirectory is null
                || libraryDirectory is null
                || !string.Equals(commonDirectory.Name, "common", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(steamAppsDirectory.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return libraryDirectory.FullName;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
