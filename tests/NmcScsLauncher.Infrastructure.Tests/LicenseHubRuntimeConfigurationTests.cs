using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class LicenseHubRuntimeConfigurationTests
{
    [Fact]
    public void DefaultsUseFinalLicenseHubValues()
    {
        Assert.Equal(
            "https://licensehub.nmc-it-service.cloud",
            LicenseHubRuntimeConfiguration.DefaultBaseUrl);
        Assert.Equal(
            "NMC-SCS-LAUNCHER",
            LicenseHubRuntimeConfiguration.DefaultProductSlug);
    }

    [Fact]
    public void FromEnvironmentUsesFinalDefaultsWhenNoOverridesAreSet()
    {
        var previousBaseUrl = Environment.GetEnvironmentVariable("NMC_LICENSEHUB_BASE_URL");
        var previousProductSlug = Environment.GetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_SLUG");
        var previousProductApiKey = Environment.GetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_BASE_URL", null);
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_SLUG", null);
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_API_KEY", null);

            var configuration = LicenseHubRuntimeConfiguration.FromEnvironment();

            Assert.Equal(LicenseHubRuntimeConfiguration.DefaultBaseUrl, configuration.BaseUrl);
            Assert.Equal(LicenseHubRuntimeConfiguration.DefaultProductSlug, configuration.ProductSlug);
            Assert.Null(configuration.ProductApiKey);
            Assert.True(configuration.HasEndpointConfiguration);
            Assert.False(configuration.HasLicenseConfiguration);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_BASE_URL", previousBaseUrl);
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_SLUG", previousProductSlug);
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_API_KEY", previousProductApiKey);
        }
    }

    [Fact]
    public void BuildRequirementForcesEnforcementWithoutEnvironmentFlag()
    {
        var previousRequired = Environment.GetEnvironmentVariable("NMC_LICENSEHUB_REQUIRED");
        try
        {
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_REQUIRED", null);

            var configuration = LicenseHubRuntimeConfiguration.FromEnvironment(requiredByBuild: true);

            Assert.True(configuration.Required);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_REQUIRED", previousRequired);
        }
    }

    [Fact]
    public void BuiltInProductApiKeyTakesPriorityOverEnvironmentProvisioning()
    {
        var previousProductApiKey = Environment.GetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_API_KEY", "environment-key");

            var configuration = LicenseHubRuntimeConfiguration.FromEnvironment(
                requiredByBuild: true,
                builtInProductApiKey: "embedded-key");

            Assert.Equal("embedded-key", configuration.ProductApiKey);
            Assert.True(configuration.HasLicenseConfiguration);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_API_KEY", previousProductApiKey);
        }
    }

    [Fact]
    public void ProductApiKeyCompletesLicenseConfigurationWithPublicDefaults()
    {
        var configuration = new LicenseHubRuntimeConfiguration(
            true,
            LicenseHubRuntimeConfiguration.DefaultBaseUrl,
            LicenseHubRuntimeConfiguration.DefaultProductSlug,
            "unit-test-api-key");

        Assert.True(configuration.HasEndpointConfiguration);
        Assert.True(configuration.HasLicenseConfiguration);
        var client = configuration.CreateClientConfiguration();
        Assert.Equal("unit-test-api-key", client.ProductApiKey);
    }

    [Fact]
    public void StoredProductApiKeyCanCompleteClientConfigurationAtRuntime()
    {
        var configuration = new LicenseHubRuntimeConfiguration(
            true,
            LicenseHubRuntimeConfiguration.DefaultBaseUrl,
            LicenseHubRuntimeConfiguration.DefaultProductSlug,
            null);

        Assert.False(configuration.HasLicenseConfiguration);
        var client = configuration.CreateClientConfiguration("stored-api-key");
        Assert.Equal("stored-api-key", client.ProductApiKey);
    }
}
