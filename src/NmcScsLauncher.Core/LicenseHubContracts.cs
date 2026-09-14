namespace NmcScsLauncher.Core;

public enum LicenseAccessState
{
    Active,
    Blocked,
    Expired,
    NotActivated,
    ActivationLimitReached,
    LicenseNotFound,
    InvalidProductCredentials,
    Unavailable,
    ProtocolError
}

public sealed record LicenseAccessSnapshot(
    LicenseAccessState State,
    bool LicenseRequired,
    string Message,
    DateTimeOffset? ExpiresAt = null,
    int? MaxActivations = null,
    int? CurrentActivations = null,
    string? CustomerName = null,
    string? CustomerEmail = null)
{
    public bool AllowsUse => State == LicenseAccessState.Active;
}

public interface ILicenseHubLicenseClient
{
    Task<LicenseAccessSnapshot> ActivateAsync(
        string licenseKey,
        string machineId,
        string deviceName,
        string appVersion,
        CancellationToken cancellationToken = default);

    Task<LicenseAccessSnapshot> ValidateAsync(
        string licenseKey,
        string machineId,
        string appVersion,
        CancellationToken cancellationToken = default);

    Task DeactivateAsync(
        string licenseKey,
        string machineId,
        CancellationToken cancellationToken = default);
}

public sealed record LicenseHubUpdateInfo(
    bool UpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string Channel,
    bool Mandatory,
    string? DownloadEndpoint,
    string? Sha256,
    string? Changelog,
    DateTimeOffset? ReleasedAt);

public sealed record LicenseHubDownloadedUpdate(
    string FilePath,
    string Sha256,
    long Length);

public interface ILicenseHubUpdateClient
{
    Task<LicenseHubUpdateInfo> CheckForUpdateAsync(
        string licenseKey,
        string machineId,
        string currentVersion,
        string channel = "stable",
        CancellationToken cancellationToken = default);

    Task<LicenseHubDownloadedUpdate> DownloadAndVerifyAsync(
        LicenseHubUpdateInfo update,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public interface IMachineIdentityProvider
{
    Task<string> GetMachineIdAsync(CancellationToken cancellationToken = default);
}
