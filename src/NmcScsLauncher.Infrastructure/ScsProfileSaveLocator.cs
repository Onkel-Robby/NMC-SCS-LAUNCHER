using System.Text;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsProfileSaveLocator : IScsProfileSaveLocator
{
    private readonly SteamLibraryLocator _steamLibraries;

    // Unit tests and callers that only inspect an explicit document home do not
    // need to depend on the Windows Steam installation.
    public ScsProfileSaveLocator() : this(new SteamLibraryLocator(Array.Empty<string>()))
    {
    }

    public ScsProfileSaveLocator(SteamLibraryLocator steamLibraries)
    {
        _steamLibraries = steamLibraries ?? throw new ArgumentNullException(nameof(steamLibraries));
    }

    public Task<IReadOnlyList<ScsProfileReference>> FindProfilesAsync(
        GameType gameType,
        string gameHomeDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameHomeDirectory))
            throw new ArgumentException("SCS-Home-Verzeichnis ist erforderlich.", nameof(gameHomeDirectory));

        cancellationToken.ThrowIfCancellationRequested();

        var home = Path.GetFullPath(gameHomeDirectory);
        if (!Directory.Exists(home))
            return Task.FromResult<IReadOnlyList<ScsProfileReference>>(Array.Empty<ScsProfileReference>());

        var profiles = new List<ScsProfileReference>();
        AddProfiles(profiles, gameType, home, "profiles", ScsProfileStorageKind.Local, cancellationToken);
        AddProfiles(profiles, gameType, home, "steam_profiles", ScsProfileStorageKind.SteamCloud, cancellationToken);

        return Task.FromResult<IReadOnlyList<ScsProfileReference>>(
            profiles
                .OrderBy(profile => profile.StorageKind)
                .ThenBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public Task<IReadOnlyList<ScsProfileReference>> FindSteamCloudProfilesAsync(
        GameType gameType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var found = new List<ScsProfileReference>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appId = GameDefinition.For(gameType).SteamAppId.ToString();

        foreach (var steamRoot in _steamLibraries.FindLibraryRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var userdataRoot = Path.Combine(steamRoot, "userdata");
            if (!IsRegularDirectory(userdataRoot))
                continue;

            // Look only at the immediate Steam account directories. Never recurse
            // through arbitrary disks or other applications' cloud data.
            foreach (var accountDirectory in EnumerateDirectoriesSafe(userdataRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsRegularDirectory(accountDirectory)
                    || !IsNumericDirectoryName(accountDirectory))
                    continue;

                var remoteRoot = Path.Combine(accountDirectory, appId, "remote");
                if (!IsRegularDirectory(remoteRoot)
                    || !IsRegularDirectory(Path.Combine(remoteRoot, "profiles")))
                    continue;

                var profiles = new List<ScsProfileReference>();
                AddProfiles(profiles, gameType, remoteRoot, "profiles",
                    ScsProfileStorageKind.SteamCloudRemote, cancellationToken);

                foreach (var profile in profiles)
                {
                    if (visited.Add(profile.ProfileDirectory))
                    {
                        found.Add(profile with
                        {
                            DisplayName = profile.DisplayName + " (Steam Cloud)"
                        });
                    }
                }
            }
        }

        return Task.FromResult<IReadOnlyList<ScsProfileReference>>(found
            .OrderBy(static profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    private static bool IsNumericDirectoryName(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return name.Length > 0 && name.All(static ch => ch >= '0' && ch <= '9');
    }

    private static bool IsRegularDirectory(string path)
    {
        try
        {
            return Directory.Exists(path)
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateDirectoriesSafe(string path)
    {
        try
        {
            return Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    public Task<IReadOnlyList<ScsSaveReference>> FindSavesAsync(
        ScsProfileReference profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        cancellationToken.ThrowIfCancellationRequested();

        var profileDirectory = Path.GetFullPath(profile.ProfileDirectory);
        var expectedProfileSii = Path.Combine(profileDirectory, "profile.sii");
        if (!PathsEqual(expectedProfileSii, profile.ProfileSiiPath) || !File.Exists(expectedProfileSii))
            throw new ScsSaveEditException("Die Profilauswahl ist nicht mehr gültig.");

        var saveRoot = Path.Combine(profileDirectory, "save");
        if (!Directory.Exists(saveRoot))
            return Task.FromResult<IReadOnlyList<ScsSaveReference>>(Array.Empty<ScsSaveReference>());

        var saves = new List<ScsSaveReference>();
        foreach (var directory in Directory.EnumerateDirectories(saveRoot, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            var gameSii = Path.Combine(info.FullName, "game.sii");
            if (!File.Exists(gameSii))
                continue;

            var kind = ScsSaveKindClassifier.Classify(info.Name);
            saves.Add(new ScsSaveReference(
                profile.GameType,
                profileDirectory,
                info.FullName,
                info.Name,
                GetSaveDisplayName(info.Name, kind),
                kind,
                gameSii,
                File.GetLastWriteTimeUtc(gameSii)));
        }

        return Task.FromResult<IReadOnlyList<ScsSaveReference>>(
            saves
                .OrderByDescending(save => save.LastWriteTimeUtc)
                .ThenBy(save => save.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static void AddProfiles(
        ICollection<ScsProfileReference> target,
        GameType gameType,
        string homeDirectory,
        string folderName,
        ScsProfileStorageKind storageKind,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(homeDirectory, folderName);
        if (!Directory.Exists(root))
            return;

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            var profileSii = Path.Combine(info.FullName, "profile.sii");
            if (!File.Exists(profileSii))
                continue;

            target.Add(new ScsProfileReference(
                gameType,
                storageKind,
                homeDirectory,
                info.FullName,
                info.Name,
                DecodeProfileDirectoryName(info.Name) ?? info.Name,
                profileSii));
        }
    }

    private static string? DecodeProfileDirectoryName(string directoryName)
    {
        if (directoryName.Length == 0 || directoryName.Length % 2 != 0)
            return null;

        try
        {
            var bytes = Convert.FromHexString(directoryName);
            var text = new UTF8Encoding(false, true).GetString(bytes).Trim();
            return text.Length > 0 && !text.Any(char.IsControl) ? text : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static string GetSaveDisplayName(string directoryName, ScsSaveKind kind) => kind switch
    {
        ScsSaveKind.AutoSave => directoryName.Equals("autosave", StringComparison.OrdinalIgnoreCase)
            ? "Autosave"
            : $"Autosave ({directoryName})",
        ScsSaveKind.QuickSave => directoryName.Equals("quicksave", StringComparison.OrdinalIgnoreCase)
            ? "Quicksave"
            : $"Quicksave ({directoryName})",
        ScsSaveKind.Manual => $"Save {directoryName}",
        _ => directoryName
    };

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
}

internal static class ScsSaveKindClassifier
{
    // The same directory rule must protect both save discovery and live writes.
    public static ScsSaveKind Classify(string? directoryName)
    {
        if (string.IsNullOrEmpty(directoryName))
            return ScsSaveKind.Unknown;
        if (directoryName.StartsWith("autosave", StringComparison.OrdinalIgnoreCase))
            return ScsSaveKind.AutoSave;
        if (directoryName.StartsWith("quicksave", StringComparison.OrdinalIgnoreCase))
            return ScsSaveKind.QuickSave;
        if (directoryName.All(char.IsDigit))
            return ScsSaveKind.Manual;

        return ScsSaveKind.Unknown;
    }
}
