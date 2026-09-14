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
        }
        finally
        {
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_BASE_URL", previousBaseUrl);
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_PRODUCT_SLUG", previousProductSlug);
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
    public void PublicDefaultsAloneDoNotCountAsPartialConfiguration()
    {
        var configuration = new LicenseHubRuntimeConfiguration(
            false,
            LicenseHubRuntimeConfiguration.DefaultBaseUrl,
            LicenseHubRuntimeConfiguration.DefaultProductSlug,
            null);

        Assert.False(configuration.HasAnyConfiguration);
        Assert.False(configuration.HasLicenseConfiguration);
    }

    [Fact]
    public void ProductApiKeyCompletesLicenseConfigurationWithPublicDefaults()
    {
        var configuration = new LicenseHubRuntimeConfiguration(
            false,
            LicenseHubRuntimeConfiguration.DefaultBaseUrl,
            LicenseHubRuntimeConfiguration.DefaultProductSlug,
            "unit-test-api-key");

        Assert.True(configuration.HasAnyConfiguration);
        Assert.True(configuration.HasLicenseConfiguration);
    }

    [Fact]
    public void ExplicitAlternateBaseUrlCountsAsConfiguration()
    {
        var configuration = new LicenseHubRuntimeConfiguration(
            false,
            "https://staging.example.test",
            LicenseHubRuntimeConfiguration.DefaultProductSlug,
            null);

        Assert.True(configuration.HasAnyConfiguration);
        Assert.False(configuration.HasLicenseConfiguration);
    }

    [Fact]
    public void ExplicitAlternateProductSlugCountsAsConfiguration()
    {
        var configuration = new LicenseHubRuntimeConfiguration(
            false,
            LicenseHubRuntimeConfiguration.DefaultBaseUrl,
            "NMC-SCS-LAUNCHER-STAGING",
            null);

        Assert.True(configuration.HasAnyConfiguration);
        Assert.False(configuration.HasLicenseConfiguration);
    }
}
