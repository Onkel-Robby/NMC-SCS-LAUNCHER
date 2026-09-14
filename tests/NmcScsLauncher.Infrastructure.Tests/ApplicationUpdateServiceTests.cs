using NmcScsLauncher.Core;
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
        var service = new ApplicationUpdateService(
            new LicenseHubRuntimeConfiguration(false, null, null, null),
            new MemoryCredentialStore(),
            new FixedMachineIdentityProvider(),
            httpClient);

        Assert.False(service.IsConfigured);
        await Assert.ThrowsAsync<LicenseHubConfigurationException>(() => service.CheckAsync("0.7.0-dev"));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ConfiguredRuntimeWithoutStoredLicenseRejectsCheckWithoutNetwork()
    {
        var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        var service = new ApplicationUpdateService(
            new LicenseHubRuntimeConfiguration(true, "https://licensehub.example/", "nmc-scs-launcher", "test-value"),
            new MemoryCredentialStore(),
            new FixedMachineIdentityProvider(),
            httpClient);

        Assert.True(service.IsConfigured);
        await Assert.ThrowsAsync<LicenseHubConfigurationException>(() => service.CheckAsync("0.7.0-dev"));
        Assert.Equal(0, handler.RequestCount);
    }

    private sealed class MemoryCredentialStore : ILicenseCredentialStore
    {
        public Task<string?> LoadLicenseKeyAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SaveLicenseKeyAsync(string licenseKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClearLicenseKeyAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FixedMachineIdentityProvider : IMachineIdentityProvider
    {
        public Task<string> GetMachineIdAsync(CancellationToken cancellationToken = default) => Task.FromResult("machine-value");
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        }
    }
}
