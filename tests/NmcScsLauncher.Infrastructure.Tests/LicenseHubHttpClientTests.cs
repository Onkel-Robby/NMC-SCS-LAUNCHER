using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class LicenseHubHttpClientTests
{
    [Fact]
    public async Task ActivateUsesDeployedLicenseHubContractAndReturnsActiveState()
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
                  "message": "License activated successfully",
                  "license": {
                    "expires_at": "2027-01-01T00:00:00+00:00",
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
        Assert.True(result.AllowsUse);
        Assert.Equal(2, result.MaxActivations);
        Assert.Equal(1, result.CurrentActivations);
        Assert.Equal(new Uri("https://license.example.test/api/license/activate.php"), requestUri);

        using var json = JsonDocument.Parse(requestBody!);
        var root = json.RootElement;
        Assert.Equal("nmc-scs-launcher", root.GetProperty("product_slug").GetString());
        Assert.Equal("product-value", root.GetProperty("api_key").GetString());
        Assert.Equal("license-value", root.GetProperty("license_key").GetString());
        Assert.Equal("machine-value", root.GetProperty("machine_id").GetString());
        Assert.Equal("Test-PC", root.GetProperty("device_name").GetString());
        Assert.Equal("0.7.0-dev", root.GetProperty("app_version").GetString());
    }

    [Fact]
    public async Task ValidateMapsBlockedLicenseToFailClosedState()
    {
        var handler = new StubHandler(_ => Task.FromResult(Json(HttpStatusCode.Forbidden, """
            {"success":false,"error":"License is blocked","status":"blocked"}
            """)));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var result = await client.ValidateAsync("license-value", "machine-value", "0.7.0-dev");

        Assert.Equal(LicenseAccessState.Blocked, result.State);
        Assert.False(result.AllowsUse);
    }

    [Fact]
    public async Task DesktopUpdateCheckUsesLicenseAndMachineBindingWithoutBearerToken()
    {
        string? body = null;
        var handler = new StubHandler(async request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("/api/desktop-update/check.php", request.RequestUri!.AbsolutePath);
            body = await request.Content!.ReadAsStringAsync();
            return Json(HttpStatusCode.OK, """
                {
                  "success": true,
                  "update_available": true,
                  "current_version": "0.7.0-dev",
                  "version": "0.8.0",
                  "channel": "stable",
                  "mandatory": false,
                  "download_endpoint": "/api/desktop-update/download.php?release_id=10&license_id=20&activation_id=30&expires=9999999999&channel=stable&sig=test",
                  "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "changelog": "Test release",
                  "released_at": "2026-09-14T00:00:00+00:00"
                }
                """);
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var update = await client.CheckForUpdateAsync("license-value", "machine-value", "0.7.0-dev");

        Assert.True(update.UpdateAvailable);
        Assert.Equal("0.8.0", update.LatestVersion);
        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.Equal("nmc-scs-launcher", root.GetProperty("product_slug").GetString());
        Assert.Equal("product-value", root.GetProperty("api_key").GetString());
        Assert.Equal("license-value", root.GetProperty("license_key").GetString());
        Assert.Equal("machine-value", root.GetProperty("machine_id").GetString());
        Assert.Equal("stable", root.GetProperty("channel").GetString());
    }

    [Fact]
    public async Task DownloadVerifiesSha256BeforePublishingFile()
    {
        var payload = Encoding.UTF8.GetBytes("verified update payload");
        var sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var handler = new StubHandler(request =>
        {
            Assert.Equal("/api/desktop-update/download.php", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            });
        });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var destination = Path.Combine(root, "update.bin");
        var update = new LicenseHubUpdateInfo(
            true,
            "0.7.0-dev",
            "0.8.0",
            "stable",
            false,
            "/api/desktop-update/download.php?release_id=10&license_id=20&activation_id=30&expires=9999999999&channel=stable&sig=test",
            sha,
            null,
            null);

        var result = await client.DownloadAndVerifyAsync(update, destination);

        Assert.True(File.Exists(destination));
        Assert.Equal(sha, result.Sha256);
        Assert.Equal(payload, await File.ReadAllBytesAsync(destination));
    }

    private static LicenseHubHttpClient CreateClient(HttpClient httpClient) =>
        new(httpClient, new LicenseHubClientConfiguration(
            "https://license.example.test/",
            "nmc-scs-launcher",
            "product-value",
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
