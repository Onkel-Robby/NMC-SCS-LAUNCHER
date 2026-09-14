using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class LicenseHubRuntimeConfigurationTests
{
    [Fact]
    public void DefaultBaseUrlUsesFinalLicenseHubDomain()
    {
        Assert.Equal(
            "https://licensehub.nmc-it-service.cloud",
            LicenseHubRuntimeConfiguration.DefaultBaseUrl);
    }

    [Fact]
    public void FromEnvironmentUsesFinalDomainWhenNoOverrideIsSet()
    {
        var previous = Environment.GetEnvironmentVariable("NMC_LICENSEHUB_BASE_URL");
        try
        {
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_BASE_URL", null);

            var configuration = LicenseHubRuntimeConfiguration.FromEnvironment();

            Assert.Equal(LicenseHubRuntimeConfiguration.DefaultBaseUrl, configuration.BaseUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NMC_LICENSEHUB_BASE_URL", previous);
        }
    }

    [Fact]
    public void DefaultBaseUrlAloneDoesNotCountAsPartialConfiguration()
    {
        var configuration = new LicenseHubRuntimeConfiguration(
            false,
            LicenseHubRuntimeConfiguration.DefaultBaseUrl,
            null,
            null);

        Assert.False(configuration.HasAnyConfiguration);
        Assert.False(configuration.HasLicenseConfiguration);
    }

    [Fact]
    public void ExplicitAlternateBaseUrlCountsAsConfiguration()
    {
        var configuration = new LicenseHubRuntimeConfiguration(
            false,
            "https://staging.example.test",
            null,
            null);

        Assert.True(configuration.HasAnyConfiguration);
        Assert.False(configuration.HasLicenseConfiguration);
    }
}
