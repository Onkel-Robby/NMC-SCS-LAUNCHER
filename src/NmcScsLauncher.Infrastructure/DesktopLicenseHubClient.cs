using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class DesktopLicenseHubClient : ILicenseHubLicenseClient
{
    private readonly HttpClient _httpClient;
    private readonly LicenseHubClientConfiguration _configuration;
    private readonly Uri _baseUri;

    public DesktopLicenseHubClient(HttpClient httpClient, LicenseHubClientConfiguration configuration)
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
        ValidateArguments(licenseKey, machineId);
        var request = new LicenseRequest(
            _configuration.ProductSlug.Trim(),
            licenseKey.Trim(),
            machineId.Trim(),
            NormalizeOptional(deviceName, 255),
            NormalizeOptional(appVersion, 50));
        return SendAsync("api/desktop-license/activate.php", request, cancellationToken);
    }

    public Task<LicenseAccessSnapshot> ValidateAsync(
        string licenseKey,
        string machineId,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateArguments(licenseKey, machineId);
        var request = new LicenseRequest(
            _configuration.ProductSlug.Trim(),
            licenseKey.Trim(),
            machineId.Trim(),
            null,
            NormalizeOptional(appVersion, 50));
        return SendAsync("api/desktop-license/validate.php", request, cancellationToken);
    }

    public async Task DeactivateAsync(
        string licenseKey,
        string machineId,
        CancellationToken cancellationToken = default)
    {
        ValidateArguments(licenseKey, machineId);
        var request = new DeactivateRequest(
            _configuration.ProductSlug.Trim(),
            licenseKey.Trim(),
            machineId.Trim());

        using var response = await PostAsync("api/desktop-license/deactivate.php", request, cancellationToken);
        var envelope = await ReadEnvelopeAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode || envelope.Success != true)
            throw new LicenseHubProtocolException(
                envelope.Message ?? envelope.Error ?? $"LicenseHub deactivation failed with HTTP {(int)response.StatusCode}.");
    }

    private async Task<LicenseAccessSnapshot> SendAsync(
        string path,
        LicenseRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await PostAsync(path, request, cancellationToken);
            var envelope = await ReadEnvelopeAsync(response, cancellationToken);
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

            return MapFailure(response.StatusCode, envelope);
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

    private async Task<HttpResponseMessage> PostAsync<T>(
        string relativePath,
        T body,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_configuration.EffectiveTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, relativePath));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("NMC-SCS-LAUNCHER/0.7");
        request.Content = JsonContent.Create(body);
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    }

    private static async Task<LicenseEnvelope> ReadEnvelopeAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<LicenseEnvelope>(stream, JsonOptions, cancellationToken)
            ?? throw new JsonException("LicenseHub returned an empty response.");
    }

    private static LicenseAccessSnapshot MapFailure(HttpStatusCode statusCode, LicenseEnvelope envelope)
    {
        var remoteStatus = envelope.Status?.Trim().ToLowerInvariant();
        var state = remoteStatus switch
        {
            "blocked" => LicenseAccessState.Blocked,
            "expired" => LicenseAccessState.Expired,
            "not_activated" => LicenseAccessState.NotActivated,
            "activation_limit_reached" => LicenseAccessState.ActivationLimitReached,
            _ when statusCode == HttpStatusCode.NotFound => LicenseAccessState.LicenseNotFound,
            _ => LicenseAccessState.ProtocolError
        };

        return new LicenseAccessSnapshot(
            state,
            true,
            envelope.Message ?? envelope.Error ?? $"LicenseHub rejected the request with HTTP {(int)statusCode}.",
            null,
            envelope.MaxActivations,
            envelope.CurrentActivations);
    }

    private static LicenseAccessSnapshot Unavailable(string message) =>
        new(LicenseAccessState.Unavailable, true, message);

    private static LicenseAccessSnapshot ProtocolFailure(string message) =>
        new(LicenseAccessState.ProtocolError, true, message);

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) return null;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static void ValidateArguments(string licenseKey, string machineId)
    {
        if (string.IsNullOrWhiteSpace(licenseKey) || licenseKey.Trim().Length > 100)
            throw new ArgumentException("License key is missing or invalid.", nameof(licenseKey));
        if (string.IsNullOrWhiteSpace(machineId) || machineId.Trim().Length > 255)
            throw new ArgumentException("Machine ID is missing or invalid.", nameof(machineId));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record LicenseRequest(
        [property: JsonPropertyName("product_slug")] string ProductSlug,
        [property: JsonPropertyName("license_key")] string LicenseKey,
        [property: JsonPropertyName("machine_id")] string MachineId,
        [property: JsonPropertyName("device_name")] string? DeviceName,
        [property: JsonPropertyName("app_version")] string? AppVersion);

    private sealed record DeactivateRequest(
        [property: JsonPropertyName("product_slug")] string ProductSlug,
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
}
