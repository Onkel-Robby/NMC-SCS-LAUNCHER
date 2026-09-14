using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class LicenseHubHttpClient : ILicenseHubLicenseClient, ILicenseHubUpdateClient
{
    private readonly HttpClient _httpClient;
    private readonly LicenseHubClientConfiguration _configuration;
    private readonly Uri _baseUri;

    public LicenseHubHttpClient(HttpClient httpClient, LicenseHubClientConfiguration configuration)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configuration.ValidateLicenseConfiguration();
        _baseUri = _configuration.GetValidatedBaseUri();
    }

    public Task<LicenseAccessSnapshot> ActivateAsync(
        string licenseKey,
        string machineId,
        string deviceName,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateLicenseArguments(licenseKey, machineId);
        var request = new LicenseRequest(
            _configuration.ProductSlug.Trim(),
            _configuration.ProductApiKey.Trim(),
            licenseKey.Trim(),
            machineId.Trim(),
            NormalizeOptional(deviceName, 255),
            NormalizeOptional(appVersion, 50));
        return SendLicenseRequestAsync("api/license/activate.php", request, cancellationToken);
    }

    public Task<LicenseAccessSnapshot> ValidateAsync(
        string licenseKey,
        string machineId,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateLicenseArguments(licenseKey, machineId);
        var request = new LicenseRequest(
            _configuration.ProductSlug.Trim(),
            _configuration.ProductApiKey.Trim(),
            licenseKey.Trim(),
            machineId.Trim(),
            null,
            NormalizeOptional(appVersion, 50));
        return SendLicenseRequestAsync("api/license/validate.php", request, cancellationToken);
    }

    public async Task DeactivateAsync(
        string licenseKey,
        string machineId,
        CancellationToken cancellationToken = default)
    {
        ValidateLicenseArguments(licenseKey, machineId);
        var request = new DeactivateRequest(
            _configuration.ProductSlug.Trim(),
            _configuration.ProductApiKey.Trim(),
            licenseKey.Trim(),
            machineId.Trim());

        using var response = await SendAsync(HttpMethod.Post, "api/license/deactivate.php", request, cancellationToken);
        var envelope = await ReadLicenseEnvelopeAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode || envelope.Success != true)
            throw new LicenseHubProtocolException(envelope.Error ?? $"LicenseHub deactivation failed with HTTP {(int)response.StatusCode}.");
    }

    public async Task<LicenseHubUpdateInfo> CheckForUpdateAsync(
        string licenseKey,
        string machineId,
        string currentVersion,
        string channel = "stable",
        CancellationToken cancellationToken = default)
    {
        ValidateLicenseArguments(licenseKey, machineId);
        if (string.IsNullOrWhiteSpace(currentVersion) || currentVersion.Trim().Length > 50)
            throw new ArgumentException("Current version is missing or invalid.", nameof(currentVersion));

        channel = string.IsNullOrWhiteSpace(channel) ? "stable" : channel.Trim().ToLowerInvariant();
        if (channel.Length > 20)
            throw new ArgumentException("Update channel is invalid.", nameof(channel));

        var normalizedVersion = currentVersion.Trim();

        // The deployed LicenseHub update endpoint authenticates the product, not the machine.
        // Therefore independently validate the stored license + machine immediately before
        // asking for release metadata. This keeps update access gated by the same license
        // authority without requiring any LicenseHub server changes.
        var licenseState = await ValidateAsync(
            licenseKey,
            machineId,
            normalizedVersion,
            cancellationToken);
        if (!licenseState.AllowsUse)
            throw new LicenseHubProtocolException(
                $"LicenseHub denied update access because the license is not active ({licenseState.State}).");

        var relativePath = BuildExistingUpdateCheckPath(normalizedVersion, channel);
        using var timeout = CreateTimeoutToken(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("NMC-SCS-LAUNCHER/0.7");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        var envelope = await ReadUpdateEnvelopeAsync(response, timeout.Token);

        if (!response.IsSuccessStatusCode || envelope.Success != true)
            throw new LicenseHubProtocolException(
                envelope.Message ?? envelope.Error ?? $"LicenseHub update check failed with HTTP {(int)response.StatusCode}.");

        var updateAvailable = envelope.UpdateAvailable == true;
        var latestVersion = envelope.LatestVersion?.Trim();
        if (string.IsNullOrWhiteSpace(latestVersion))
        {
            if (updateAvailable)
                throw new LicenseHubProtocolException("LicenseHub reports an update without a version.");
            latestVersion = normalizedVersion;
        }

        if (updateAvailable)
        {
            if (string.IsNullOrWhiteSpace(envelope.DownloadUrl))
                throw new LicenseHubProtocolException("LicenseHub reports an update without a download URL.");
            _ = NormalizeSha256(envelope.Sha256);
            _ = ResolveDownloadUri(envelope.DownloadUrl);
        }

        return new LicenseHubUpdateInfo(
            updateAvailable,
            envelope.CurrentVersion?.Trim() ?? normalizedVersion,
            latestVersion,
            envelope.Channel?.Trim() ?? channel,
            envelope.Mandatory == true,
            envelope.DownloadUrl,
            envelope.Sha256,
            envelope.Changelog,
            null);
    }

    public async Task<LicenseHubDownloadedUpdate> DownloadAndVerifyAsync(
        LicenseHubUpdateInfo update,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!update.UpdateAvailable)
            throw new InvalidOperationException("No LicenseHub update is available for download.");
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Update destination path is required.", nameof(destinationPath));

        var expectedHash = NormalizeSha256(update.Sha256);
        var downloadUri = ResolveDownloadUri(update.DownloadEndpoint);
        var finalPath = Path.GetFullPath(destinationPath.Trim());
        if (File.Exists(finalPath) || Directory.Exists(finalPath))
            throw new IOException("The update destination already exists.");
        var parent = Path.GetDirectoryName(finalPath);
        if (string.IsNullOrWhiteSpace(parent))
            throw new IOException("The update destination directory is invalid.");
        Directory.CreateDirectory(parent);

        var stagingPath = finalPath + ".partial-" + Guid.NewGuid().ToString("N");
        using var timeout = CreateTimeoutToken(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, downloadUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        long written = 0;
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token))
        await using (var destination = new FileStream(
                         stagingPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         128 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[128 * 1024];
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(), timeout.Token);
                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                hasher.AppendData(buffer, 0, read);
                written += read;
                if (total is > 0)
                    progress?.Report(Math.Clamp(written * 100d / total.Value, 0d, 100d));
            }
            await destination.FlushAsync(timeout.Token);
        }

        var actualHash = hasher.GetHashAndReset();
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            TryDeletePartial(stagingPath);
            throw new LicenseHubProtocolException("Downloaded LicenseHub update failed SHA-256 verification.");
        }

        File.Move(stagingPath, finalPath);
        progress?.Report(100d);
        return new LicenseHubDownloadedUpdate(finalPath, Convert.ToHexString(actualHash).ToLowerInvariant(), written);
    }

    private string BuildExistingUpdateCheckPath(string currentVersion, string channel)
    {
        static string Escape(string value) => Uri.EscapeDataString(value);

        return "api/update/check.php"
            + "?product_slug=" + Escape(_configuration.ProductSlug.Trim())
            + "&api_key=" + Escape(_configuration.ProductApiKey.Trim())
            + "&version=" + Escape(currentVersion)
            + "&channel=" + Escape(channel);
    }

    private async Task<LicenseAccessSnapshot> SendLicenseRequestAsync(
        string path,
        LicenseRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, path, request, cancellationToken);
            var envelope = await ReadLicenseEnvelopeAsync(response, cancellationToken);
            if (response.IsSuccessStatusCode && envelope.Success == true)
            {
                if (!string.Equals(envelope.Status, "active", StringComparison.OrdinalIgnoreCase))
                    return ProtocolFailure("LicenseHub returned a successful response without active status.");

                return new LicenseAccessSnapshot(
                    LicenseAccessState.Active,
                    envelope.LicenseRequired ?? true,
                    envelope.Message ?? "License is active.",
                    ParseDate(envelope.License?.ExpiresAt),
                    envelope.License?.MaxActivations,
                    envelope.License?.CurrentActivations,
                    envelope.License?.CustomerName,
                    envelope.License?.CustomerEmail);
            }

            return MapLicenseFailure(response.StatusCode, envelope);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("LicenseHub request timed out.");
        }
        catch (HttpRequestException)
        {
            return Unavailable("LicenseHub is currently unreachable.");
        }
        catch (JsonException)
        {
            return ProtocolFailure("LicenseHub returned an invalid JSON response.");
        }
    }

    private async Task<HttpResponseMessage> SendAsync<T>(
        HttpMethod method,
        string relativePath,
        T body,
        CancellationToken cancellationToken)
    {
        using var timeout = CreateTimeoutToken(cancellationToken);
        var request = new HttpRequestMessage(method, new Uri(_baseUri, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("NMC-SCS-LAUNCHER/0.7");
        request.Content = JsonContent.Create(body);
        try
        {
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        }
        finally
        {
            request.Dispose();
        }
    }

    private CancellationTokenSource CreateTimeoutToken(CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(_configuration.EffectiveTimeout);
        return source;
    }

    private static async Task<LicenseEnvelope> ReadLicenseEnvelopeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<LicenseEnvelope>(stream, JsonOptions, cancellationToken)
            ?? throw new JsonException("LicenseHub returned an empty response.");
    }

    private static async Task<UpdateEnvelope> ReadUpdateEnvelopeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<UpdateEnvelope>(stream, JsonOptions, cancellationToken)
            ?? throw new JsonException("LicenseHub returned an empty update response.");
    }

    private static LicenseAccessSnapshot MapLicenseFailure(HttpStatusCode statusCode, LicenseEnvelope envelope)
    {
        var remoteStatus = envelope.Status?.Trim().ToLowerInvariant();
        var state = remoteStatus switch
        {
            "blocked" => LicenseAccessState.Blocked,
            "expired" => LicenseAccessState.Expired,
            "not_activated" => LicenseAccessState.NotActivated,
            "activation_limit_reached" => LicenseAccessState.ActivationLimitReached,
            _ when statusCode == HttpStatusCode.NotFound => LicenseAccessState.LicenseNotFound,
            _ when statusCode == HttpStatusCode.Unauthorized => LicenseAccessState.InvalidProductCredentials,
            _ => LicenseAccessState.ProtocolError
        };

        return new LicenseAccessSnapshot(
            state,
            true,
            envelope.Error ?? envelope.Message ?? $"LicenseHub rejected the request with HTTP {(int)statusCode}.",
            null,
            envelope.MaxActivations,
            envelope.CurrentActivations);
    }

    private static LicenseAccessSnapshot Unavailable(string message) =>
        new(LicenseAccessState.Unavailable, true, message);

    private static LicenseAccessSnapshot ProtocolFailure(string message) =>
        new(LicenseAccessState.ProtocolError, true, message);

    private Uri ResolveDownloadUri(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new LicenseHubProtocolException("LicenseHub update download URL is missing.");

        Uri resolved;
        if (Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var absolute))
        {
            if (!SameOrigin(_baseUri, absolute))
                throw new LicenseHubProtocolException("LicenseHub update download URL points to an unexpected origin.");
            resolved = absolute;
        }
        else
        {
            resolved = new Uri(_baseUri, endpoint.TrimStart('/'));
        }

        if (!string.Equals(resolved.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new LicenseHubProtocolException("LicenseHub update download URL must use HTTPS.");
        return resolved;
    }

    private static bool SameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase)
        && left.Port == right.Port;

    private static byte[] NormalizeSha256(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[7..];
        if (normalized.Length != 64 || normalized.Any(ch => !Uri.IsHexDigit(ch)))
            throw new LicenseHubProtocolException("LicenseHub update checksum is not a valid SHA-256 digest.");
        return Convert.FromHexString(normalized);
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static void ValidateLicenseArguments(string licenseKey, string machineId)
    {
        if (string.IsNullOrWhiteSpace(licenseKey) || licenseKey.Trim().Length > 100)
            throw new ArgumentException("License key is missing or invalid.", nameof(licenseKey));
        if (string.IsNullOrWhiteSpace(machineId) || machineId.Trim().Length > 255)
            throw new ArgumentException("Machine ID is missing or invalid.", nameof(machineId));
    }

    private static void TryDeletePartial(string stagingPath)
    {
        try
        {
            if (File.Exists(stagingPath)) File.Delete(stagingPath);
        }
        catch
        {
            // A .partial file is never executed; cleanup is best effort only.
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record LicenseRequest(
        [property: JsonPropertyName("product_slug")] string ProductSlug,
        [property: JsonPropertyName("api_key")] string ApiKey,
        [property: JsonPropertyName("license_key")] string LicenseKey,
        [property: JsonPropertyName("machine_id")] string MachineId,
        [property: JsonPropertyName("device_name")] string? DeviceName,
        [property: JsonPropertyName("app_version")] string? AppVersion);

    private sealed record DeactivateRequest(
        [property: JsonPropertyName("product_slug")] string ProductSlug,
        [property: JsonPropertyName("api_key")] string ApiKey,
        [property: JsonPropertyName("license_key")] string LicenseKey,
        [property: JsonPropertyName("machine_id")] string MachineId);

    private sealed class LicenseEnvelope
    {
        [JsonPropertyName("success")] public bool? Success { get; init; }
        [JsonPropertyName("error")] public string? Error { get; init; }
        [JsonPropertyName("message")] public string? Message { get; init; }
        [JsonPropertyName("status")] public string? Status { get; init; }
        [JsonPropertyName("license_required")] public bool? LicenseRequired { get; init; }
        [JsonPropertyName("max_activations")] public int? MaxActivations { get; init; }
        [JsonPropertyName("current_activations")] public int? CurrentActivations { get; init; }
        [JsonPropertyName("license")] public LicenseData? License { get; init; }
    }

    private sealed class LicenseData
    {
        [JsonPropertyName("expires_at")] public string? ExpiresAt { get; init; }
        [JsonPropertyName("max_activations")] public int? MaxActivations { get; init; }
        [JsonPropertyName("current_activations")] public int? CurrentActivations { get; init; }
        [JsonPropertyName("customer_name")] public string? CustomerName { get; init; }
        [JsonPropertyName("customer_email")] public string? CustomerEmail { get; init; }
    }

    private sealed class UpdateEnvelope
    {
        [JsonPropertyName("success")] public bool? Success { get; init; }
        [JsonPropertyName("error")] public string? Error { get; init; }
        [JsonPropertyName("message")] public string? Message { get; init; }
        [JsonPropertyName("update_available")] public bool? UpdateAvailable { get; init; }
        [JsonPropertyName("current_version")] public string? CurrentVersion { get; init; }
        [JsonPropertyName("latest_version")] public string? LatestVersion { get; init; }
        [JsonPropertyName("channel")] public string? Channel { get; init; }
        [JsonPropertyName("mandatory")] public bool? Mandatory { get; init; }
        [JsonPropertyName("download_url")] public string? DownloadUrl { get; init; }
        [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
        [JsonPropertyName("changelog")] public string? Changelog { get; init; }
    }
}
