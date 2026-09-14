using System.Diagnostics;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ApplicationUpdateService : IApplicationUpdateService
{
    private const string UpdaterFileName = "NmcScsLauncher.Updater.exe";
    private const string LauncherFileName = "NmcScsLauncher.App.exe";
    private readonly ILicenseCredentialStore _credentialStore;
    private readonly IMachineIdentityProvider _machineIdentityProvider;
    private readonly ILicenseHubUpdateClient? _updateClient;

    public ApplicationUpdateService(
        LicenseHubRuntimeConfiguration runtimeConfiguration,
        ILicenseCredentialStore credentialStore,
        IMachineIdentityProvider machineIdentityProvider,
        HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(runtimeConfiguration);
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _machineIdentityProvider = machineIdentityProvider ?? throw new ArgumentNullException(nameof(machineIdentityProvider));
        ArgumentNullException.ThrowIfNull(httpClient);

        if (runtimeConfiguration.HasLicenseConfiguration)
        {
            _updateClient = new LicenseHubHttpClient(
                httpClient,
                runtimeConfiguration.CreateClientConfiguration());
        }
    }

    public bool IsConfigured => _updateClient is not null;

    public async Task<LicenseHubUpdateInfo> CheckAsync(
        string currentVersion,
        CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        var licenseKey = await _credentialStore.LoadLicenseKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(licenseKey))
            throw new LicenseHubConfigurationException(
                "A stored LicenseHub license is required before desktop updates can be checked.");

        var machineId = await _machineIdentityProvider.GetMachineIdAsync(cancellationToken);
        return await client.CheckForUpdateAsync(
            licenseKey,
            machineId,
            currentVersion,
            "stable",
            cancellationToken);
    }

    public async Task<PreparedApplicationUpdate> DownloadAsync(
        LicenseHubUpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!update.UpdateAvailable)
            throw new InvalidOperationException("No application update is available.");

        var root = GetUpdateRoot();
        var downloads = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(downloads);
        var safeVersion = SanitizeFileName(update.LatestVersion);
        var destination = Path.Combine(
            downloads,
            $"NMC-SCS-LAUNCHER-{safeVersion}-{Guid.NewGuid():N}.zip");

        var downloaded = await RequireClient().DownloadAndVerifyAsync(
            update,
            destination,
            progress,
            cancellationToken);
        return new PreparedApplicationUpdate(update, downloaded);
    }

    public Task<int> StartApplyAsync(
        PreparedApplicationUpdate preparedUpdate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparedUpdate);
        cancellationToken.ThrowIfCancellationRequested();

        if (!preparedUpdate.Update.UpdateAvailable)
            throw new InvalidOperationException("Prepared update is not marked as available.");
        if (!File.Exists(preparedUpdate.Download.FilePath))
            throw new FileNotFoundException("Downloaded update package no longer exists.", preparedUpdate.Download.FilePath);
        if (!string.Equals(Path.GetExtension(preparedUpdate.Download.FilePath), ".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Application updater requires a ZIP package.");

        var appDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var updaterSource = Path.Combine(appDirectory, UpdaterFileName);
        if (!File.Exists(updaterSource))
            throw new FileNotFoundException(
                "The external updater executable is missing from the application directory.",
                updaterSource);

        var runnerDirectory = Path.Combine(GetUpdateRoot(), "Runner", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runnerDirectory);
        var updaterCopy = Path.Combine(runnerDirectory, UpdaterFileName);
        File.Copy(updaterSource, updaterCopy, overwrite: false);

        var startInfo = new ProcessStartInfo
        {
            FileName = updaterCopy,
            WorkingDirectory = runnerDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--package");
        startInfo.ArgumentList.Add(Path.GetFullPath(preparedUpdate.Download.FilePath));
        startInfo.ArgumentList.Add("--sha256");
        startInfo.ArgumentList.Add(preparedUpdate.Download.Sha256);
        startInfo.ArgumentList.Add("--target");
        startInfo.ArgumentList.Add(appDirectory);
        startInfo.ArgumentList.Add("--restart");
        startInfo.ArgumentList.Add(LauncherFileName);
        startInfo.ArgumentList.Add("--wait-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("External updater process could not be started.");
        return Task.FromResult(process.Id);
    }

    private ILicenseHubUpdateClient RequireClient() =>
        _updateClient ?? throw new LicenseHubConfigurationException(
            "LicenseHub update integration is not configured for this build.");

    private static string GetUpdateRoot()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NMC Network",
            "NMC SCS LAUNCHER",
            "Updates");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string SanitizeFileName(string version)
    {
        var value = string.IsNullOrWhiteSpace(version) ? "unknown" : version.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value.Length > 80 ? value[..80] : value;
    }
}
