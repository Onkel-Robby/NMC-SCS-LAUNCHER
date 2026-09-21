using System.Text;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsProfileSaveLocator : IScsProfileSaveLocator
{
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

            var kind = GetSaveKind(info.Name);
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

    private static ScsSaveKind GetSaveKind(string directoryName)
    {
        if (directoryName.StartsWith("autosave", StringComparison.OrdinalIgnoreCase))
            return ScsSaveKind.AutoSave;
        if (directoryName.StartsWith("quicksave", StringComparison.OrdinalIgnoreCase))
            return ScsSaveKind.QuickSave;
        if (directoryName.All(char.IsDigit))
            return ScsSaveKind.Manual;

        return ScsSaveKind.Unknown;
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
