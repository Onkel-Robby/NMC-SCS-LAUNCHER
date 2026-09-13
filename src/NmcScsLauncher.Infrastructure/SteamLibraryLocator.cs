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
            TryReadRegistryValue(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath", candidates);
            TryReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", candidates);
            TryReadRegistryValue(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath", candidates);
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            candidates.Add(Path.Combine(programFilesX86, "Steam"));
        }

        return candidates;
    }

    [SupportedOSPlatform("windows")]
    private static void TryReadRegistryValue(RegistryKey root, string subKeyPath, string valueName, ISet<string> candidates)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyPath);
            if (key?.GetValue(valueName) is string path && !string.IsNullOrWhiteSpace(path))
            {
                candidates.Add(path.Replace('/', Path.DirectorySeparatorChar));
            }
        }
        catch (Exception)
        {
            // Registry discovery is best-effort. Other candidates remain available.
        }
    }

    private static void ReadConfiguredLibraries(string steamRoot, ISet<string> libraries)
    {
        var configPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
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

        var modernMatches = ModernPathRegex().Matches(content);
        foreach (Match match in modernMatches)
        {
            AddVdfPath(libraries, match.Groups["path"].Value);
        }

        if (modernMatches.Count > 0)
        {
            return;
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
