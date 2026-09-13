using System.Text.RegularExpressions;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed partial class SteamGameInstallationDetector : IGameInstallationDetector
{
    private readonly SteamLibraryLocator _libraryLocator;

    public SteamGameInstallationDetector(SteamLibraryLocator libraryLocator)
    {
        _libraryLocator = libraryLocator ?? throw new ArgumentNullException(nameof(libraryLocator));
    }

    public GameInstallation? Validate(
        GameType gameType,
        string installPath,
        GameInstallationSource source = GameInstallationSource.ManualSelection)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        var definition = GameDefinition.For(gameType);
        string normalizedPath;

        try
        {
            normalizedPath = Path.GetFullPath(installPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        var executablePath = Path.Combine(normalizedPath, definition.ExecutableRelativePath);
        return File.Exists(executablePath)
            ? new GameInstallation(gameType, normalizedPath, executablePath, source)
            : null;
    }

    public Task<GameInstallation?> DetectAsync(
        GameType gameType,
        string? preferredInstallPath = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(preferredInstallPath))
        {
            var saved = Validate(gameType, preferredInstallPath, GameInstallationSource.SavedPath);
            if (saved is not null)
            {
                return Task.FromResult<GameInstallation?>(saved);
            }
        }

        var definition = GameDefinition.For(gameType);
        foreach (var libraryRoot in _libraryLocator.FindLibraryRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var steamAppsPath = Path.Combine(libraryRoot, "steamapps");
            var manifestPath = Path.Combine(steamAppsPath, $"appmanifest_{definition.SteamAppId}.acf");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var installDirectoryName = ReadInstallDirectoryName(manifestPath) ?? definition.DefaultInstallDirectoryName;
            var installPath = Path.Combine(steamAppsPath, "common", installDirectoryName);
            var installation = Validate(gameType, installPath, GameInstallationSource.SteamAutoDetection);
            if (installation is not null)
            {
                return Task.FromResult<GameInstallation?>(installation);
            }
        }

        return Task.FromResult<GameInstallation?>(null);
    }

    private static string? ReadInstallDirectoryName(string manifestPath)
    {
        try
        {
            var content = File.ReadAllText(manifestPath);
            var match = InstallDirRegex().Match(content);
            return match.Success ? match.Groups["dir"].Value : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex("\\\"installdir\\\"\\s+\\\"(?<dir>[^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InstallDirRegex();
}
