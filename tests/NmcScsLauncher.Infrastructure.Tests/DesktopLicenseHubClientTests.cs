using System.Net;
using System.Text;
using System.Text.Json;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class DesktopLicenseHubClientTests
{
    [Fact]
    public async Task ActivateUsesDesktopEndpointWithoutProductApiKey()
    {
        string? requestBody = null;
        Uri? requestUri = null;
        var handler = new StubHandler(async request =>
        {
            requestUri = request.RequestUri;
            requestBody = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.Created, """
                {
                  "success": true,
                  "status": "active",
                  "license_required": true,
                  "license": {
                    "expires_at": null,
                    "max_activations": 2,
                    "current_activations": 1
                  }
                }
                """);
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.ActivateAsync("license-value", "machine-value", "Test-PC", "0.7.0-dev");

        Assert.Equal(LicenseAccessState.Active, result.State);
        Assert.Equal(new Uri("https://license.example.test/api/desktop-license/activate.php"), requestUri);

        using var json = JsonDocument.Parse(requestBody!);
        var root = json.RootElement;
        Assert.Equal("nmc-scs-launcher", root.GetProperty("product_slug").GetString());
        Assert.False(root.TryGetProperty("api_key", out _));
        Assert.Equal("license-value", root.GetProperty("license_key").GetString());
        Assert.Equal("machine-value", root.GetProperty("machine_id").GetString());
    }

    [Fact]
    public async Task ValidateUsesDesktopEndpointAndMapsBlockedLicense()
    {
        Uri? requestUri = null;
        var handler = new StubHandler(request =>
        {
            requestUri = request.RequestUri;
            return Task.FromResult(Json(HttpStatusCode.Forbidden, """
                {"success":false,"error":"LICENSE_BLOCKED","message":"License is blocked","status":"blocked"}
                """));
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.ValidateAsync("license-value", "machine-value", "0.7.0-dev");

        Assert.Equal(new Uri("https://license.example.test/api/desktop-license/validate.php"), requestUri);
        Assert.Equal(LicenseAccessState.Blocked, result.State);
        Assert.False(result.AllowsUse);
    }

    [Fact]
    public async Task DeactivateUsesDesktopEndpointWithoutProductApiKey()
    {
        string? requestBody = null;
        Uri? requestUri = null;
        var handler = new StubHandler(async request =>
        {
            requestUri = request.RequestUri;
            requestBody = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, """
                {"success":true,"license_required":true,"message":"Activation removed successfully"}
                """);
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await client.DeactivateAsync("license-value", "machine-value");

        Assert.Equal(new Uri("https://license.example.test/api/desktop-license/deactivate.php"), requestUri);
        using var json = JsonDocument.Parse(requestBody!);
        Assert.False(json.RootElement.TryGetProperty("api_key", out _));
    }

    private static DesktopLicenseHubClient CreateClient(HttpClient httpClient) =>
        new(httpClient, new LicenseHubClientConfiguration(
            "https://license.example.test/",
            "nmc-scs-launcher",
            LicenseHubRuntimeConfiguration.DesktopPublicClientMarker,
            TimeSpan.FromSeconds(5)));

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _handler(request);
    }
}
