using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ApplicationUpdateServiceTests
{
    [Fact]
    public async Task UnconfiguredRuntimeSkipsNetworkAndRejectsChecks()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var configuration = new LicenseHubRuntimeConfiguration(
            Required: false,
            BaseUrl: null,
            ProductSlug: null,
            ProductApiKey: null,
            UpdateApiToken: null);
        var service = new ApplicationUpdateService(configuration, httpClient);

        Assert.False(service.IsConfigured);
        await Assert.ThrowsAsync<LicenseHubConfigurationException>(() =>
            service.CheckAsync("0.7.0-dev"));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public void UpdateRuntimeRequiresBothLicenseConfigurationAndUpdateToken()
    {
        using var httpClient = new HttpClient(new CountingHandler());

        var withoutToken = new ApplicationUpdateService(
            new LicenseHubRuntimeConfiguration(
                Required: false,
                BaseUrl: "https://licensehub.example/",
                ProductSlug: "nmc-scs-launcher",
                ProductApiKey: "product-api-key",
                UpdateApiToken: null),
            httpClient);

        var withToken = new ApplicationUpdateService(
            new LicenseHubRuntimeConfiguration(
                Required: false,
                BaseUrl: "https://licensehub.example/",
                ProductSlug: "nmc-scs-launcher",
                ProductApiKey: "product-api-key",
                UpdateApiToken: "update-api-token"),
            httpClient);

        Assert.False(withoutToken.IsConfigured);
        Assert.True(withToken.IsConfigured);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        }
    }
}
