using System.Reflection;

namespace NmcScsLauncher.Infrastructure;

public sealed record LicenseHubRuntimeConfiguration(
    bool Required,
    string? BaseUrl,
    string? ProductSlug,
    string? ProductApiKey)
{
    public const string DefaultBaseUrl = "https://licensehub.nmc-it-service.cloud";
    public const string DefaultProductSlug = "NMC-SCS-LAUNCHER";
    private const string EmbeddedProductApiKeyMetadataName = "NmcLicenseHubProductApiKey";

    public bool HasAnyConfiguration =>
        Required
        || !string.IsNullOrWhiteSpace(ProductApiKey)
        || (!string.IsNullOrWhiteSpace(BaseUrl)
            && !string.Equals(BaseUrl.TrimEnd('/'), DefaultBaseUrl, StringComparison.OrdinalIgnoreCase))
        || (!string.IsNullOrWhiteSpace(ProductSlug)
            && !string.Equals(ProductSlug, DefaultProductSlug, StringComparison.Ordinal));

    public bool HasEndpointConfiguration =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(ProductSlug);

    public bool HasLicenseConfiguration =>
        HasEndpointConfiguration
        && !string.IsNullOrWhiteSpace(ProductApiKey);

    public LicenseHubClientConfiguration CreateClientConfiguration(string? productApiKey = null)
    {
        var effectiveApiKey = string.IsNullOrWhiteSpace(productApiKey)
            ? ProductApiKey
            : productApiKey.Trim();

        if (!HasEndpointConfiguration || string.IsNullOrWhiteSpace(effectiveApiKey))
            throw new LicenseHubConfigurationException("LicenseHub license configuration is incomplete.");

        return new LicenseHubClientConfiguration(
            BaseUrl!,
            ProductSlug!,
            effectiveApiKey!);
    }

    public static LicenseHubRuntimeConfiguration FromEnvironment(
        bool requiredByBuild = false,
        string? builtInProductApiKey = null)
    {
        var requiredValue = Environment.GetEnvironmentVariable("NMC_LICENSEHUB_REQUIRED")?.Trim();
        var requiredByEnvironment = string.Equals(requiredValue, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(requiredValue, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(requiredValue, "yes", StringComparison.OrdinalIgnoreCase);

        var releaseProductApiKey =
            Normalize(builtInProductApiKey)
            ?? ReadEmbeddedProductApiKey();

        return new LicenseHubRuntimeConfiguration(
            requiredByBuild || requiredByEnvironment,
            Normalize(Environment.GetEnvironmentVariable("NMC_LICENSEHUB_BASE_URL")) ?? DefaultBaseUrl,
            Normalize(Environment.GetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_SLUG")) ?? DefaultProductSlug,
            releaseProductApiKey
                ?? Normalize(Environment.GetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_API_KEY")));
    }

    private static string? ReadEmbeddedProductApiKey()
    {
        var attribute = typeof(LicenseHubRuntimeConfiguration)
            .Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static item =>
                string.Equals(
                    item.Key,
                    EmbeddedProductApiKeyMetadataName,
                    StringComparison.Ordinal));

        return Normalize(attribute?.Value);
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
