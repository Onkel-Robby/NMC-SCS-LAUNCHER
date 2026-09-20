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
            }
            catch (Exception)
            {
                // Registry discovery is best-effort. Other views/candidates remain available.
            }
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
