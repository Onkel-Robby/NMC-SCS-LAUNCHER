namespace NmcScsLauncher.Infrastructure;

public sealed record LicenseHubRuntimeConfiguration(
    bool Required,
    string? BaseUrl,
    string? ProductSlug,
    string? ProductApiKey,
    string? UpdateApiToken)
{
    public bool HasAnyConfiguration =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        || !string.IsNullOrWhiteSpace(ProductSlug)
        || !string.IsNullOrWhiteSpace(ProductApiKey)
        || !string.IsNullOrWhiteSpace(UpdateApiToken);

    public bool HasLicenseConfiguration =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ProductSlug)
        && !string.IsNullOrWhiteSpace(ProductApiKey);

    public LicenseHubClientConfiguration CreateClientConfiguration()
    {
        if (!HasLicenseConfiguration)
            throw new LicenseHubConfigurationException("LicenseHub license configuration is incomplete.");

        return new LicenseHubClientConfiguration(
            BaseUrl!,
            ProductSlug!,
            ProductApiKey!,
            string.IsNullOrWhiteSpace(UpdateApiToken) ? null : UpdateApiToken.Trim());
    }

    public static LicenseHubRuntimeConfiguration FromEnvironment()
    {
        var requiredValue = Environment.GetEnvironmentVariable("NMC_LICENSEHUB_REQUIRED")?.Trim();
        var required = string.Equals(requiredValue, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(requiredValue, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(requiredValue, "yes", StringComparison.OrdinalIgnoreCase);

        return new LicenseHubRuntimeConfiguration(
            required,
            Normalize(Environment.GetEnvironmentVariable("NMC_LICENSEHUB_BASE_URL")),
            Normalize(Environment.GetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_SLUG")),
            Normalize(Environment.GetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_API_KEY")),
            Normalize(Environment.GetEnvironmentVariable("NMC_LICENSEHUB_UPDATE_API_TOKEN")));
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
