namespace NmcScsLauncher.Infrastructure;

public sealed record LicenseHubClientConfiguration(
    string BaseUrl,
    string ProductSlug,
    string ProductApiKey,
    string? UpdateApiToken = null,
    TimeSpan? Timeout = null)
{
    public Uri GetValidatedBaseUri()
    {
        if (!Uri.TryCreate(BaseUrl?.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new LicenseHubConfigurationException("LicenseHub base URL must be a credential-free HTTPS URL.");
        }

        var builder = new UriBuilder(uri)
        {
            Path = uri.AbsolutePath.TrimEnd('/') + "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }

    public void ValidateLicenseConfiguration()
    {
        _ = GetValidatedBaseUri();
        if (string.IsNullOrWhiteSpace(ProductSlug) || ProductSlug.Trim().Length > 100)
            throw new LicenseHubConfigurationException("LicenseHub product slug is missing or invalid.");
        if (string.IsNullOrWhiteSpace(ProductApiKey) || ProductApiKey.Trim().Length > 64)
            throw new LicenseHubConfigurationException("LicenseHub product API key is missing or invalid.");
    }

    public string GetValidatedUpdateToken()
    {
        ValidateLicenseConfiguration();
        var token = UpdateApiToken?.Trim() ?? string.Empty;
        if (token.Length == 0)
            throw new LicenseHubConfigurationException("LicenseHub update API token is not configured.");
        return token;
    }

    public TimeSpan EffectiveTimeout => Timeout is { } value && value > TimeSpan.Zero
        ? value
        : TimeSpan.FromSeconds(10);
}

public sealed class LicenseHubConfigurationException : Exception
{
    public LicenseHubConfigurationException(string message) : base(message)
    {
    }
}

public sealed class LicenseHubProtocolException : Exception
{
    public LicenseHubProtocolException(string message) : base(message)
    {
    }

    public LicenseHubProtocolException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
