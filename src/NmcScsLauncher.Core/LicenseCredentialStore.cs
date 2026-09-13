namespace NmcScsLauncher.Core;

public interface ILicenseCredentialStore
{
    Task<string?> LoadLicenseKeyAsync(CancellationToken cancellationToken = default);

    Task SaveLicenseKeyAsync(string licenseKey, CancellationToken cancellationToken = default);

    Task ClearLicenseKeyAsync(CancellationToken cancellationToken = default);
}
