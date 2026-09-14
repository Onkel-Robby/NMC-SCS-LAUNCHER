namespace NmcScsLauncher.Infrastructure;

public sealed record LicenseHubRuntimeConfiguration(
    bool Required,
    string? BaseUrl,
    string? ProductSlug,
    string? ProductApiKey)
{
    public const string DefaultBaseUrl = "https://licensehub.nmc-it-service.cloud";
    public const string DefaultProductSlug = "NMC-SCS-LAUNCHER";

    public bool HasAnyConfiguration =>
        Required
        || !string.IsNullOrWhiteSpace(ProductApiKey)
        || (!string.IsNullOrWhiteSpace(BaseUrl)
            && !string.Equals(BaseUrl.TrimEnd('/'), DefaultBaseUrl, StringComparison.OrdinalIgnoreCase))
        || (!string.IsNullOrWhiteSpace(ProductSlug)
            && !string.Equals(ProductSlug, DefaultProductSlug, StringComparison.Ordinal));

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
            ProductApiKey!);
    }

    public static LicenseHubRuntimeConfiguration FromEnvironment()
    {
        var requiredValue = Environment.GetEnvironmentVariable("NMC_LICENSEHUB_REQUIRED")?.Trim();
        var required = string.Equals(requiredValue, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(requiredValue, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(requiredValue, "yes", StringComparison.OrdinalIgnoreCase);

        return new LicenseHubRuntimeConfiguration(
            required,
            Normalize(Environment.GetEnvironmentVariable("NMC_LICENSEHUB_BASE_URL")) ?? DefaultBaseUrl,
            Normalize(Environment.GetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_SLUG")) ?? DefaultProductSlug,
            Normalize(Environment.GetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_API_KEY")));
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
