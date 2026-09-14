namespace NmcScsLauncher.Core;

public sealed record PreparedApplicationUpdate(
    LicenseHubUpdateInfo Update,
    LicenseHubDownloadedUpdate Download);

public interface IApplicationUpdateService
{
    Task<LicenseHubUpdateInfo> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken = default);

    Task<PreparedApplicationUpdate> DownloadAsync(
        LicenseHubUpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    Task<int> StartApplyAsync(
        PreparedApplicationUpdate preparedUpdate,
        CancellationToken cancellationToken = default);
}
