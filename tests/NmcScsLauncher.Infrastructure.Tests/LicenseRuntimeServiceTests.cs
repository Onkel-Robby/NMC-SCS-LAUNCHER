using System.Net;
using System.Text;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class LicenseRuntimeServiceTests
{
    [Fact]
    public async Task DevelopmentBuildWithoutConfigurationAllowsUse()
    {
        var service = new LicenseRuntimeService(
            new LicenseHubRuntimeConfiguration(false, null, null, null),
            new MemoryCredentialStore(),
            new FixedMachineIdentityProvider(),
            new HttpClient(new StaticHandler(HttpStatusCode.OK, "{}")));

        var state = await service.InitializeAsync("0.7.0-dev");

        Assert.Equal(LicenseRuntimeState.DevelopmentBypass, state.State);
        Assert.False(state.EnforcementEnabled);
        Assert.True(state.AllowsUse);
    }

    [Fact]
    public async Task RequiredBuildWithoutConfigurationFailsClosed()
    {
        var service = new LicenseRuntimeService(
            new LicenseHubRuntimeConfiguration(true, null, null, null),
            new MemoryCredentialStore(),
            new FixedMachineIdentityProvider(),
            new HttpClient(new StaticHandler(HttpStatusCode.OK, "{}")));

        var state = await service.InitializeAsync("0.7.0");

        Assert.Equal(LicenseRuntimeState.ConfigurationError, state.State);
        Assert.True(state.EnforcementEnabled);
        Assert.False(state.AllowsUse);
    }

    [Fact]
    public async Task RequiredBuildWithoutStoredLicenseIsBlocked()
    {
        var service = CreateRequiredService(
            new MemoryCredentialStore(),
            new StaticHandler(HttpStatusCode.OK, "{}"));

        var state = await service.InitializeAsync("0.7.0");

        Assert.Equal(LicenseRuntimeState.LicenseMissing, state.State);
        Assert.False(state.AllowsUse);
    }

    [Fact]
    public async Task StoredActiveLicenseAllowsUseAfterServerValidation()
    {
        var store = new MemoryCredentialStore("stored-license-value");
        var service = CreateRequiredService(
            store,
            new StaticHandler(HttpStatusCode.OK, """
                {
                  "success": true,
                  "status": "active",
                  "license_required": true,
                  "license": {
                    "expires_at": "2027-01-01T00:00:00+00:00",
                    "max_activations": 2,
                    "current_activations": 1,
                    "customer_name": "Test Customer"
                  }
                }
                """));

        var state = await service.InitializeAsync("0.7.0");

        Assert.Equal(LicenseRuntimeState.Active, state.State);
        Assert.True(state.AllowsUse);
        Assert.Equal("Test Customer", state.CustomerName);
    }

    [Fact]
    public async Task BlockedLicenseRemainsFailClosed()
    {
        var store = new MemoryCredentialStore("stored-license-value");
        var service = CreateRequiredService(
            store,
            new StaticHandler(HttpStatusCode.Forbidden, """
                {"success":false,"error":"License is blocked","status":"blocked"}
                """));

        var state = await service.InitializeAsync("0.7.0");

        Assert.Equal(LicenseRuntimeState.Blocked, state.State);
        Assert.False(state.AllowsUse);
    }

    [Fact]
    public async Task ActivationStoresKeyOnlyAfterActiveResponse()
    {
        var store = new MemoryCredentialStore();
        var service = CreateRequiredService(
            store,
            new StaticHandler(HttpStatusCode.Created, """
                {
                  "success":true,
                  "status":"active",
                  "license":{"expires_at":null,"max_activations":1,"current_activations":1}
                }
                """));

        var state = await service.ActivateAsync("new-license-value", "Test PC", "0.7.0");

        Assert.True(state.AllowsUse);
        Assert.Equal("new-license-value", await store.LoadLicenseKeyAsync());
    }

    private static LicenseRuntimeService CreateRequiredService(
        MemoryCredentialStore store,
        HttpMessageHandler handler)
    {
        return new LicenseRuntimeService(
            new LicenseHubRuntimeConfiguration(
                true,
                "https://license.example.test/",
                "nmc-scs-launcher",
                "product-value"),
            store,
            new FixedMachineIdentityProvider(),
            new HttpClient(handler));
    }

    private sealed class MemoryCredentialStore : ILicenseCredentialStore
    {
        private string? _licenseKey;

        public MemoryCredentialStore(string? initialValue = null)
        {
            _licenseKey = initialValue;
        }

        public Task<string?> LoadLicenseKeyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_licenseKey);

        public Task SaveLicenseKeyAsync(string licenseKey, CancellationToken cancellationToken = default)
        {
            _licenseKey = licenseKey;
            return Task.CompletedTask;
        }

        public Task ClearLicenseKeyAsync(CancellationToken cancellationToken = default)
        {
            _licenseKey = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedMachineIdentityProvider : IMachineIdentityProvider
    {
        public Task<string> GetMachineIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("machine-value");
    }

    private sealed class StaticHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _json;

        public StaticHandler(HttpStatusCode statusCode, string json)
        {
            _statusCode = statusCode;
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            });
    }
}
