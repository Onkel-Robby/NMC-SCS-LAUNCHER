namespace NmcScsLauncher.Core;

public interface ILicenseCredentialStore
{
    Task<string?> LoadLicenseKeyAsync(CancellationToken cancellationToken = default);

    Task SaveLicenseKeyAsync(string licenseKey, CancellationToken cancellationToken = default);

    Task ClearLicenseKeyAsync(CancellationToken cancellationToken = default);
}

public interface IProductApiCredentialStore
{
    Task<string?> LoadProductApiKeyAsync(CancellationToken cancellationToken = default);

    Task SaveProductApiKeyAsync(string productApiKey, CancellationToken cancellationToken = default);

    Task ClearProductApiKeyAsync(CancellationToken cancellationToken = default);
}
