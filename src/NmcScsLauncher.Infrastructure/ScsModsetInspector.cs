using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsModsetInspector : IModsetInspector
{
    public Task<ModsetInspection> InspectAsync(Modset modset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modset);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.Run(() => Inspect(modset, cancellationToken), cancellationToken);
    }

    private static ModsetInspection Inspect(Modset modset, CancellationToken cancellationToken)
    {
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

            return new ModsetInspection(
                gameDataDirectory,
                modDirectory,
                localProfilesDirectory,
                steamProfilesDirectory,
                GameDataDirectoryExists: false,
                PackageModCount: 0,
                ExtractedModCount: 0,
                Profiles: Array.Empty<ScsProfileInfo>(),
                Warnings: [warning],
                ModItems: Array.Empty<LocalModInfo>());
        }

        var mods = ReadMods(modDirectory, warnings, cancellationToken);
        var packageModCount = mods.Count(static mod => mod.Kind == LocalModKind.ScsPackage);
        var extractedModCount = mods.Count(static mod => mod.Kind == LocalModKind.ExtractedDirectory);

        var profiles = new List<ScsProfileInfo>();
        ReadProfiles(localProfilesDirectory, ProfileStorageKind.Local, profiles, warnings, cancellationToken);
        ReadProfiles(steamProfilesDirectory, ProfileStorageKind.Steam, profiles, warnings, cancellationToken);

        return new ModsetInspection(
            gameDataDirectory,
            modDirectory,
            localProfilesDirectory,
            steamProfilesDirectory,
            GameDataDirectoryExists: true,
            packageModCount,
            extractedModCount,
            profiles.OrderBy(static profile => profile.StorageKind)
                .ThenBy(static profile => profile.DirectoryName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            warnings,
            mods);
    }

    private static IReadOnlyList<LocalModInfo> ReadMods(
        string directory,
        ICollection<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<LocalModInfo>();
        }

        var mods = new List<LocalModInfo>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.Equals(Path.GetExtension(file), ".scs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(file);
                    mods.Add(new LocalModInfo(
                        info.Name,
                        info.FullName,
                        LocalModKind.ScsPackage,
                        info.Length,
                        new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Die Mod-Datei '{Path.GetFileName(file)}' konnte nicht vollständig gelesen werden.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add("Der Mod-Ordner konnte nicht vollständig nach .scs-Dateien gelesen werden.");
        }

        try
        {
            foreach (var directoryPath in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var info = new DirectoryInfo(directoryPath);
                    var isReparsePoint = info.Attributes.HasFlag(FileAttributes.ReparsePoint);
                    var size = isReparsePoint
                        ? null
                        : TryCalculateDirectorySize(info.FullName, cancellationToken, out var complete);

                    if (isReparsePoint)
                    {
                        warnings.Add($"Die Größe des Mod-Ordners '{info.Name}' wurde nicht ermittelt, weil der Ordner ein Link/Junction ist.");
                    }
                    else if (!complete)
                    {
                        warnings.Add($"Die Größe des Mod-Ordners '{info.Name}' konnte nicht vollständig ermittelt werden.");
                    }

                    mods.Add(new LocalModInfo(
                        info.Name,
                        info.FullName,
                        LocalModKind.ExtractedDirectory,
                        size,
                        new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add($"Der Mod-Ordner '{Path.GetFileName(directoryPath)}' konnte nicht vollständig gelesen werden.");
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add("Entpackte Mods konnten nicht vollständig gelesen werden.");
        }

        return mods
            .OrderBy(static mod => mod.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(static mod => mod.Kind)
            .ToArray();
    }

    private static long? TryCalculateDirectorySize(
        string root,
        CancellationToken cancellationToken,
        out bool complete)
    {
        complete = true;
        long total = 0;
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        checked
                        {
                            total += new FileInfo(file).Length;
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OverflowException)
                    {
                        complete = false;
                    }
                }

                foreach (var child in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
                        {
                            complete = false;
                            continue;
                        }

                        pending.Push(child);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        complete = false;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                complete = false;
            }
        }

        return complete ? total : null;
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
