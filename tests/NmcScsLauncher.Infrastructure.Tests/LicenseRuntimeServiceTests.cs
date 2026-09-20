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

    [Fact]
    public async Task SuccessfulDeactivationClearsStoredLicenseOnlyAfterServerConfirmation()
    {
        var store = new MemoryCredentialStore("stored-license-value");
        var service = CreateRequiredService(
            store,
            new StaticHandler(HttpStatusCode.OK, """
                {"success":true,"message":"Activation deactivated"}
                """));

        var state = await service.DeactivateAsync();

        Assert.Equal(LicenseRuntimeState.LicenseMissing, state.State);
        Assert.False(state.AllowsUse);
        Assert.Null(await store.LoadLicenseKeyAsync());
    }

    [Fact]
    public async Task FailedDeactivationKeepsStoredLicense()
    {
        var store = new MemoryCredentialStore("stored-license-value");
        var service = CreateRequiredService(
            store,
            new StaticHandler(HttpStatusCode.Forbidden, """
                {"success":false,"error":"Deactivation denied"}
                """));

        await Assert.ThrowsAsync<LicenseHubProtocolException>(() => service.DeactivateAsync());

        Assert.Equal("stored-license-value", await store.LoadLicenseKeyAsync());
    }

    [Fact]
    public async Task ConfiguredProductApiKeyDoesNotRequireLocalProvisioning()
    {
        var licenseStore = new MemoryCredentialStore();
        var productStore = new MemoryProductApiCredentialStore();
        var service = new LicenseRuntimeService(
            new LicenseHubRuntimeConfiguration(
                true,
                "https://license.example.test/",
                "nmc-scs-launcher",
                "embedded-product-value"),
            licenseStore,
            productStore,
            new FixedMachineIdentityProvider(),
            new HttpClient(new StaticHandler(HttpStatusCode.OK, "{}")));

        var state = await service.InitializeAsync("1.0.0");

        Assert.Equal(LicenseRuntimeState.LicenseMissing, state.State);
        Assert.Null(await productStore.LoadProductApiKeyAsync());
    }

    [Fact]
    public async Task StoredProductApiKeyRemainsSupportedAsLegacyFallback()
    {
        var licenseStore = new MemoryCredentialStore("stored-license-value");
        var productStore = new MemoryProductApiCredentialStore("legacy-product-value");
        var service = new LicenseRuntimeService(
            new LicenseHubRuntimeConfiguration(
                true,
                "https://license.example.test/",
                "nmc-scs-launcher",
                null),
            licenseStore,
            productStore,
            new FixedMachineIdentityProvider(),
            new HttpClient(new StaticHandler(HttpStatusCode.OK, """
                {
                  "success": true,
                  "status": "active",
                  "license_required": true,
                  "license": {"max_activations": 1, "current_activations": 1}
                }
                """)));

        var state = await service.InitializeAsync("1.0.0");

        Assert.Equal(LicenseRuntimeState.Active, state.State);
        Assert.True(state.AllowsUse);
    }

    [Fact]
    public async Task RejectedStoredProductCredentialIsClearedAndCanBeReplaced()
    {
        var licenseStore = new MemoryCredentialStore();
        var productStore = new MemoryProductApiCredentialStore("wrong-product-value");
        var service = new LicenseRuntimeService(
            new LicenseHubRuntimeConfiguration(
                true,
                "https://license.example.test/",
                "nmc-scs-launcher",
                null),
            licenseStore,
            productStore,
            new FixedMachineIdentityProvider(),
            new HttpClient(new SequenceHandler(
                new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(
                        """{"success":false,"error":"Invalid product credentials"}""",
                        Encoding.UTF8,
                        "application/json")
                },
                new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent(
                        """{"success":true,"status":"active","license":{"max_activations":1,"current_activations":1}}""",
                        Encoding.UTF8,
                        "application/json")
                })));

        var rejected = await service.ActivateAsync("license-value", "Test PC", "1.0.0");

        Assert.Equal(LicenseRuntimeState.InvalidProductCredentials, rejected.State);
        Assert.Null(await productStore.LoadProductApiKeyAsync());

        await productStore.SaveProductApiKeyAsync("correct-product-value");
        var active = await service.ActivateAsync("license-value", "Test PC", "1.0.0");

        Assert.Equal(LicenseRuntimeState.Active, active.State);
        Assert.True(active.AllowsUse);
        Assert.Equal("license-value", await licenseStore.LoadLicenseKeyAsync());
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

    private sealed class MemoryProductApiCredentialStore : IProductApiCredentialStore
    {
        private string? _value;

        public MemoryProductApiCredentialStore(string? initialValue = null)
        {
            _value = initialValue;
        }

        public Task<string?> LoadProductApiKeyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_value);
        }

        public Task SaveProductApiKeyAsync(string productApiKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _value = productApiKey;
            return Task.CompletedTask;
        }

        public Task ClearProductApiKeyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _value = null;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedMachineIdentityProvider : IMachineIdentityProvider
    {
        public Task<string> GetMachineIdAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("machine-value");
    }

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public SequenceHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
                throw new InvalidOperationException("No HTTP response configured for this test request.");
            return Task.FromResult(_responses.Dequeue());
        }
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
