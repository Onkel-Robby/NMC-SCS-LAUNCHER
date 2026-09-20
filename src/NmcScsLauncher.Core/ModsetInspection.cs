namespace NmcScsLauncher.Core;

public enum ProfileStorageKind
{
    Local,
    Steam
}

public enum LocalModKind
{
    ScsPackage,
    ExtractedDirectory
}

public sealed record ScsProfileInfo(
    string DirectoryName,
    string DirectoryPath,
    ProfileStorageKind StorageKind);

public sealed record LocalModInfo(
    string Name,
    string Path,
    LocalModKind Kind,
    long? SizeBytes,
    DateTimeOffset LastWriteTimeUtc);

public sealed record ModsetInspection(
    string GameDataDirectory,
    string ModDirectory,
    string LocalProfilesDirectory,
    string SteamProfilesDirectory,
    bool GameDataDirectoryExists,
    int PackageModCount,
    int ExtractedModCount,
    IReadOnlyList<ScsProfileInfo> Profiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<LocalModInfo>? ModItems = null)
{
    public int TotalModCount => PackageModCount + ExtractedModCount;

    public int LocalProfileCount => Profiles.Count(static profile => profile.StorageKind == ProfileStorageKind.Local);

    public int SteamProfileCount => Profiles.Count(static profile => profile.StorageKind == ProfileStorageKind.Steam);

    public int TotalProfileCount => Profiles.Count;

    public IReadOnlyList<LocalModInfo> LocalMods => ModItems ?? Array.Empty<LocalModInfo>();
}

public interface IModsetInspector
{
    Task<ModsetInspection> InspectAsync(Modset modset, CancellationToken cancellationToken = default);
}
