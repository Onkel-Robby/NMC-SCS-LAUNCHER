using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsModsetInspector : IModsetInspector
{
    public Task<ModsetInspection> InspectAsync(Modset modset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modset);
        cancellationToken.ThrowIfCancellationRequested();

        var directMode = ScsModsetPathResolver.UsesDirectModDirectory(modset);
        var gameDataDirectory = directMode
            ? ScsModsetPathResolver.GetStandardGameDataDirectory(modset.Game)
            : ScsModsetPathResolver.GetRuntimeGameDataDirectory(modset);
        var modDirectory = ScsModsetPathResolver.GetModDirectory(modset);
        var localProfilesDirectory = ScsModsetPathResolver.GetLocalProfilesDirectory(modset);
        var steamProfilesDirectory = ScsModsetPathResolver.GetSteamProfilesDirectory(modset);
        var warnings = new List<string>();

        var inspectionRootExists = directMode
            ? Directory.Exists(modDirectory)
            : Directory.Exists(gameDataDirectory);

        if (!inspectionRootExists)
        {
            var warning = directMode
                ? "Der ausgewählte Mod-Ordner existiert nicht."
                : $"Der SCS-Datenordner '{GameDefinition.For(modset.Game).HomeDirectoryName}' existiert noch nicht.";

            return Task.FromResult(new ModsetInspection(
                gameDataDirectory,
                modDirectory,
                localProfilesDirectory,
                steamProfilesDirectory,
                GameDataDirectoryExists: false,
                PackageModCount: 0,
                ExtractedModCount: 0,
                Profiles: Array.Empty<ScsProfileInfo>(),
                Warnings: [warning]));
        }

        var packageModCount = CountScsPackages(modDirectory, warnings, cancellationToken);
        var extractedModCount = CountDirectories(modDirectory, warnings, cancellationToken);
        var profiles = new List<ScsProfileInfo>();
        ReadProfiles(localProfilesDirectory, ProfileStorageKind.Local, profiles, warnings, cancellationToken);
        ReadProfiles(steamProfilesDirectory, ProfileStorageKind.Steam, profiles, warnings, cancellationToken);

        return Task.FromResult(new ModsetInspection(
            gameDataDirectory,
            modDirectory,
            localProfilesDirectory,
            steamProfilesDirectory,
            GameDataDirectoryExists: true,
            packageModCount,
            extractedModCount,
            profiles.OrderBy(static profile => profile.StorageKind).ThenBy(static profile => profile.DirectoryName, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings));
    }

    private static int CountScsPackages(string directory, ICollection<string> warnings, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        try
        {
            var count = 0;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.Equals(Path.GetExtension(file), ".scs", StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }

            return count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add("Der Mod-Ordner konnte nicht vollständig gelesen werden.");
            return 0;
        }
    }

    private static int CountDirectories(string directory, ICollection<string> warnings, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        try
        {
            var count = 0;
            foreach (var _ in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                count++;
            }

            return count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add("Entpackte Mods konnten nicht vollständig gelesen werden.");
            return 0;
        }
    }

    private static void ReadProfiles(
        string directory,
        ProfileStorageKind storageKind,
        ICollection<ScsProfileInfo> profiles,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            foreach (var profileDirectory in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                profiles.Add(new ScsProfileInfo(
                    Path.GetFileName(profileDirectory),
                    profileDirectory,
                    storageKind));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add(storageKind == ProfileStorageKind.Steam
                ? "Steam-Profile konnten nicht vollständig gelesen werden."
                : "Lokale Profile konnten nicht vollständig gelesen werden.");
        }
    }
}
