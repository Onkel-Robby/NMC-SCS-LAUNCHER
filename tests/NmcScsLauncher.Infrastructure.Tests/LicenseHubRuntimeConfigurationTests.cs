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
        try
        {
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_BASE_URL", null);
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_SLUG", null);

            var configuration = LicenseHubRuntimeConfiguration.FromEnvironment();

            Assert.Equal(LicenseHubRuntimeConfiguration.DefaultBaseUrl, configuration.BaseUrl);
            Assert.Equal(LicenseHubRuntimeConfiguration.DefaultProductSlug, configuration.ProductSlug);
            Assert.Equal(LicenseHubRuntimeConfiguration.DesktopPublicClientMarker, configuration.ProductApiKey);
            Assert.False(configuration.Required);
            Assert.False(configuration.HasLicenseConfiguration);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_BASE_URL", previousBaseUrl);
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_SLUG", previousProductSlug);
        }
    }

    [Fact]
    public void BuildRequirementEnablesDesktopConfigurationWithoutExternalProductKey()
    {
        var previousRequired = Environment.GetEnvironmentVariable("NMC_LICENSEHUB_REQUIRED");
        var previousLegacyKey = Environment.GetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_REQUIRED", null);
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_API_KEY", null);

            var configuration = LicenseHubRuntimeConfiguration.FromEnvironment(requiredByBuild: true);

            Assert.True(configuration.Required);
            Assert.True(configuration.HasLicenseConfiguration);
            Assert.Equal(LicenseHubRuntimeConfiguration.DesktopPublicClientMarker, configuration.ProductApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_REQUIRED", previousRequired);
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_API_KEY", previousLegacyKey);
        }
    }

    [Fact]
    public void PublicDefaultsAloneDoNotEnableDevelopmentBuildIntegration()
    {
        var configuration = new LicenseHubRuntimeConfiguration(
            false,
            LicenseHubRuntimeConfiguration.DefaultBaseUrl,
            LicenseHubRuntimeConfiguration.DefaultProductSlug,
            LicenseHubRuntimeConfiguration.DesktopPublicClientMarker);

        Assert.False(configuration.HasAnyConfiguration);
        Assert.False(configuration.HasLicenseConfiguration);
    }

    [Fact]
    public void ExplicitAlternateBaseUrlEnablesDesktopConfiguration()
    {
        var configuration = new LicenseHubRuntimeConfiguration(
            false,
            "https://staging.example.test",
            LicenseHubRuntimeConfiguration.DefaultProductSlug,
            LicenseHubRuntimeConfiguration.DesktopPublicClientMarker);

        Assert.True(configuration.HasAnyConfiguration);
        Assert.True(configuration.HasLicenseConfiguration);
    }

    [Fact]
    public void ExplicitAlternateProductSlugEnablesDesktopConfiguration()
    {
        var configuration = new LicenseHubRuntimeConfiguration(
            false,
            LicenseHubRuntimeConfiguration.DefaultBaseUrl,
            "NMC-SCS-LAUNCHER-STAGING",
            LicenseHubRuntimeConfiguration.DesktopPublicClientMarker);

        Assert.True(configuration.HasAnyConfiguration);
        Assert.True(configuration.HasLicenseConfiguration);
    }
}
