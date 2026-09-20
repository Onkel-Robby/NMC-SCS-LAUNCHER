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
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(gameHomeDirectory))
            throw new ArgumentException("Game home directory is required.", nameof(gameHomeDirectory));

        var home = Path.GetFullPath(gameHomeDirectory);
        var result = new List<ScsProfileReference>();

        AddProfiles(result, gameType, home, "profiles", ScsProfileStorageKind.Local, cancellationToken);
        AddProfiles(result, gameType, home, "steam_profiles", ScsProfileStorageKind.SteamCloud, cancellationToken);

        return Task.FromResult<IReadOnlyList<ScsProfileReference>>(
            result
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

        var saveRoot = Path.Combine(profile.ProfileDirectory, "save");
        if (!Directory.Exists(saveRoot))
            return Task.FromResult<IReadOnlyList<ScsSaveReference>>(Array.Empty<ScsSaveReference>());

        var saves = new List<ScsSaveReference>();
        foreach (var saveDirectory in Directory.EnumerateDirectories(saveRoot, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = new DirectoryInfo(saveDirectory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            var gameSii = Path.Combine(info.FullName, "game.sii");
            if (!File.Exists(gameSii))
                continue;

            var kind = GetSaveKind(info.Name);
            saves.Add(new ScsSaveReference(
                profile.GameType,
                profile.ProfileDirectory,
                info.FullName,
                info.Name,
                GetSaveDisplayName(info.Name, kind),
                kind,
                gameSii,
                new DateTimeOffset(info.LastWriteTimeUtc)));
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
        var profileRoot = Path.Combine(homeDirectory, folderName);
        if (!Directory.Exists(profileRoot))
            return;

        foreach (var profileDirectory in Directory.EnumerateDirectories(profileRoot, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = new DirectoryInfo(profileDirectory);
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
                TryDecodeProfileDirectoryName(info.Name) ?? info.Name,
                profileSii));
        }
    }

    private static string? TryDecodeProfileDirectoryName(string directoryName)
    {
        if (directoryName.Length == 0 || directoryName.Length % 2 != 0)
            return null;

        try
        {
            var bytes = Convert.FromHexString(directoryName);
            var decoded = new UTF8Encoding(false, true).GetString(bytes).Trim();
            if (decoded.Length == 0 || decoded.Any(char.IsControl))
                return null;

            return decoded;
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
}
