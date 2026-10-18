using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;
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

        var registryInstallation = TryDetectFromWindowsUninstallRegistry(gameType, definition);
        if (registryInstallation is not null)
        {
            return Task.FromResult<GameInstallation?>(registryInstallation);
        }

        foreach (var libraryRoot in _libraryLocator.FindLibraryRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var steamAppsPath = Path.Combine(libraryRoot, "steamapps");
            var manifestPath = Path.Combine(steamAppsPath, $"appmanifest_{definition.SteamAppId}.acf");
            if (File.Exists(manifestPath))
            {
                var installDirectoryName = ReadInstallDirectoryName(manifestPath);
                if (!string.IsNullOrWhiteSpace(installDirectoryName))
                {
                    var manifestInstall = Validate(
                        gameType,
                        Path.Combine(steamAppsPath, "common", installDirectoryName),
                        GameInstallationSource.SteamAutoDetection);
                    if (manifestInstall is not null)
                        return Task.FromResult<GameInstallation?>(manifestInstall);
                }
            }

            // Steam may have an absent/stale appmanifest after a library move.
            // Only probe the canonical game folder in an already trusted Steam
            // library, and always require the exact game's x64 executable.
            var defaultInstall = Validate(
                gameType,
                Path.Combine(steamAppsPath, "common", definition.DefaultInstallDirectoryName),
                GameInstallationSource.SteamAutoDetection);
            if (defaultInstall is not null)
                return Task.FromResult<GameInstallation?>(defaultInstall);
        }

        return Task.FromResult<GameInstallation?>(null);
    }

    private GameInstallation? TryDetectFromWindowsUninstallRegistry(GameType gameType, GameDefinition definition)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return TryDetectFromWindowsUninstallRegistryCore(gameType, definition);
    }

    [SupportedOSPlatform("windows")]
    private GameInstallation? TryDetectFromWindowsUninstallRegistryCore(GameType gameType, GameDefinition definition)
    {
        var subKeyPath = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App {definition.SteamAppId}";

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    using var key = root.OpenSubKey(subKeyPath);
                    if (key?.GetValue("InstallLocation") is not string installLocation
                        || string.IsNullOrWhiteSpace(installLocation))
                    {
                        continue;
                    }

                    var installation = Validate(
                        gameType,
                        installLocation.Trim().Trim('"'),
                        GameInstallationSource.SteamAutoDetection);
                    if (installation is not null)
                    {
                        return installation;
                    }
                }
                catch (Exception)
                {
                    // Steam uninstall registry detection is best-effort.
                }
            }
        }

        return null;
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
