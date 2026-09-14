namespace NmcScsLauncher.Infrastructure;

public sealed record LicenseHubRuntimeConfiguration(
    bool Required,
    string? BaseUrl,
    string? ProductSlug,
    string? ProductApiKey = null)
{
    public const string DefaultBaseUrl = "https://licensehub.nmc-it-service.cloud";
    public const string DefaultProductSlug = "NMC-SCS-LAUNCHER";
    public const string DesktopPublicClientMarker = "nmc-desktop-v1";

    public bool HasAnyConfiguration =>
        Required
        || (!string.IsNullOrWhiteSpace(BaseUrl)
            && !string.Equals(BaseUrl.TrimEnd('/'), DefaultBaseUrl, StringComparison.OrdinalIgnoreCase))
        || (!string.IsNullOrWhiteSpace(ProductSlug)
            && !string.Equals(ProductSlug, DefaultProductSlug, StringComparison.Ordinal))
        || (!string.IsNullOrWhiteSpace(ProductApiKey)
            && !string.Equals(ProductApiKey, DesktopPublicClientMarker, StringComparison.Ordinal));

    public bool HasLicenseConfiguration =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ProductSlug)
        && !string.IsNullOrWhiteSpace(ProductApiKey)
        && HasAnyConfiguration;

    public LicenseHubClientConfiguration CreateClientConfiguration()
    {
        if (!HasLicenseConfiguration)
            throw new LicenseHubConfigurationException("LicenseHub desktop configuration is incomplete.");

        return new LicenseHubClientConfiguration(
            BaseUrl!,
            ProductSlug!,
            ProductApiKey!);
    }

    public static LicenseHubRuntimeConfiguration FromEnvironment(bool requiredByBuild = false)
    {
        var requiredValue = Environment.GetEnvironmentVariable("NMC_LICENSEHUB_REQUIRED")?.Trim();
        var requiredByEnvironment = string.Equals(requiredValue, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(requiredValue, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(requiredValue, "yes", StringComparison.OrdinalIgnoreCase);

        return new LicenseHubRuntimeConfiguration(
            requiredByBuild || requiredByEnvironment,
            Normalize(Environment.GetEnvironmentVariable("NMC_LICENSEHUB_BASE_URL")) ?? DefaultBaseUrl,
            Normalize(Environment.GetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_SLUG")) ?? DefaultProductSlug,
            DesktopPublicClientMarker);
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
