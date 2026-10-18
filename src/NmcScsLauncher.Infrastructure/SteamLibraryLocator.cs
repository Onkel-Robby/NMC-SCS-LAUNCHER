using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace NmcScsLauncher.Infrastructure;

public sealed partial class SteamLibraryLocator
{
    private readonly IReadOnlyList<string>? _explicitSteamRoots;

    public SteamLibraryLocator()
    {
    }

    public SteamLibraryLocator(IEnumerable<string> explicitSteamRoots)
    {
        ArgumentNullException.ThrowIfNull(explicitSteamRoots);
        _explicitSteamRoots = explicitSteamRoots.Where(static path => !string.IsNullOrWhiteSpace(path)).ToArray();
    }

    public IReadOnlyList<string> FindLibraryRoots()
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var steamRoot in GetSteamRootCandidates())
        {
            if (!Directory.Exists(steamRoot))
            {
                continue;
            }

            AddNormalized(libraries, steamRoot);
            ReadConfiguredLibraries(steamRoot, libraries);
        }

        return libraries.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private IEnumerable<string> GetSteamRootCandidates()
    {
        if (_explicitSteamRoots is not null)
        {
            return _explicitSteamRoots;
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            AddWindowsRegistrySteamCandidates(candidates);
            AddRunningSteamCandidates(candidates);
            AddCommonSteamLibraryCandidates(candidates);
        }

        AddKnownWindowsSteamFolder(candidates, Environment.SpecialFolder.ProgramFilesX86);
        AddKnownWindowsSteamFolder(candidates, Environment.SpecialFolder.ProgramFiles);

        return candidates;
    }

    private static void AddKnownWindowsSteamFolder(ISet<string> candidates, Environment.SpecialFolder specialFolder)
    {
        var basePath = Environment.GetFolderPath(specialFolder);
        if (!string.IsNullOrWhiteSpace(basePath))
        {
            candidates.Add(Path.Combine(basePath, "Steam"));
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddWindowsRegistrySteamCandidates(ISet<string> candidates)
    {
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using var currentUser = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view);
                TryReadRegistryValue(currentUser, @"Software\Valve\Steam", "SteamPath", candidates);
                TryReadRegistryValue(currentUser, @"Software\Valve\Steam", "InstallPath", candidates);
                TryReadRegistryValue(currentUser, @"Software\Valve\Steam", "SteamExe", candidates, valueIsExecutable: true);
                TryReadSteamAppPath(currentUser, candidates);
                TryReadSteamUninstallPath(currentUser, candidates);
            }
            catch (Exception)
            {
                // Registry discovery is best-effort. Other views/candidates remain available.
            }

            try
            {
                using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                TryReadRegistryValue(localMachine, @"SOFTWARE\Valve\Steam", "InstallPath", candidates);
                TryReadRegistryValue(localMachine, @"SOFTWARE\Valve\Steam", "SteamPath", candidates);
                TryReadRegistryValue(localMachine, @"SOFTWARE\Valve\Steam", "SteamExe", candidates, valueIsExecutable: true);
                TryReadSteamAppPath(localMachine, candidates);
                TryReadSteamUninstallPath(localMachine, candidates);
            }
            catch (Exception)
            {
                // Registry discovery is best-effort. Other views/candidates remain available.
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void TryReadSteamAppPath(RegistryKey root, ISet<string> candidates)
    {
        const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\steam.exe";
        TryReadRegistryValue(root, path, "Path", candidates);
        TryReadRegistryValue(root, path, string.Empty, candidates, valueIsExecutable: true);
    }

    [SupportedOSPlatform("windows")]
    private static void TryReadSteamUninstallPath(RegistryKey root, ISet<string> candidates)
    {
        const string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam";
        TryReadRegistryValue(root, path, "InstallLocation", candidates);
    }

    [SupportedOSPlatform("windows")]
    private static void AddRunningSteamCandidates(ISet<string> candidates)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("steam"))
            {
                using (process)
                {
                    try
                    {
                        var exe = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(exe))
                        {
                            var directory = Path.GetDirectoryName(exe);
                            if (!string.IsNullOrWhiteSpace(directory))
                                candidates.Add(directory);
                        }
                    }
                    catch (Exception)
                    {
                        // A protected or inaccessible process must not interrupt detection.
                    }
                }
            }
        }
        catch (Exception)
        {
            // Steam may not be running or the process list may be inaccessible.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AddCommonSteamLibraryCandidates(ISet<string> candidates)
    {
        // Bounded exact-folder probes: never recursively crawl the user's disks.
        // Steam may be installed on a different fixed drive with missing/stale
        // registry entries or a missing libraryfolders.vdf.
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed)
                    continue;

                foreach (var relativePath in new[]
                {
                    "SteamLibrary",
                    "Steam",
                    Path.Combine("Games", "SteamLibrary"),
                    Path.Combine("Games", "Steam")
                })
                {
                    try
                    {
                        var path = Path.Combine(drive.Name, relativePath);
                        if (Directory.Exists(path))
                            candidates.Add(path);
                    }
                    catch (Exception)
                    {
                        // Ignore unavailable disks and inaccessible folders.
                    }
                }
            }
        }
        catch (Exception)
        {
            // Registry and standard install locations remain available.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void TryReadRegistryValue(
        RegistryKey root,
        string subKeyPath,
        string valueName,
        ISet<string> candidates,
        bool valueIsExecutable = false)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath);
            if (key?.GetValue(valueName) is not string raw || string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            var normalized = raw.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
            if (valueIsExecutable)
            {
                normalized = Path.GetDirectoryName(normalized) ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                candidates.Add(normalized);
            }
        }
        catch (Exception)
        {
            // Registry discovery is best-effort. Other candidates remain available.
        }
    }

    private static void ReadConfiguredLibraries(string steamRoot, ISet<string> libraries)
    {
        var candidateFiles = new[]
        {
            Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"),
            Path.Combine(steamRoot, "config", "libraryfolders.vdf")
        };

        foreach (var configPath in candidateFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ReadConfiguredLibrariesFile(configPath, libraries);
        }
    }

    private static void ReadConfiguredLibrariesFile(string configPath, ISet<string> libraries)
    {
        if (!File.Exists(configPath))
        {
            return;
        }

        string content;
        try
        {
            content = File.ReadAllText(configPath);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        foreach (Match match in ModernPathRegex().Matches(content))
        {
            AddVdfPath(libraries, match.Groups["path"].Value);
        }

        foreach (Match match in LegacyPathRegex().Matches(content))
        {
            AddVdfPath(libraries, match.Groups["path"].Value);
        }
    }

    private static void AddVdfPath(ISet<string> libraries, string encodedPath)
    {
        var path = encodedPath.Replace("\\\\", "\\").Replace("\\\"", "\"");
        if (Directory.Exists(path))
        {
            AddNormalized(libraries, path);
        }
    }

    private static void AddNormalized(ISet<string> libraries, string path)
    {
        try
        {
            libraries.Add(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Ignore malformed paths from external Steam configuration.
        }
    }

    [GeneratedRegex("\\\"path\\\"\\s+\\\"(?<path>(?:\\\\.|[^\\\"])*)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModernPathRegex();

    [GeneratedRegex("^\\s*\\\"\\d+\\\"\\s+\\\"(?<path>(?:\\\\.|[^\\\"])*)\\\"\\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex LegacyPathRegex();
}
