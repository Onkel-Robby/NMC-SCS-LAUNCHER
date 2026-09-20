using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class LicenseRuntimeService : ILicenseRuntimeService
{
    private readonly LicenseHubRuntimeConfiguration _runtimeConfiguration;
    private readonly ILicenseCredentialStore _credentialStore;
    private readonly IProductApiCredentialStore _productApiCredentialStore;
    private readonly IMachineIdentityProvider _machineIdentityProvider;
    private readonly HttpClient _httpClient;
    private ILicenseHubLicenseClient? _licenseClient;

    public LicenseRuntimeService(
        LicenseHubRuntimeConfiguration runtimeConfiguration,
        ILicenseCredentialStore credentialStore,
        IProductApiCredentialStore productApiCredentialStore,
        IMachineIdentityProvider machineIdentityProvider,
        HttpClient httpClient)
    {
        _runtimeConfiguration = runtimeConfiguration ?? throw new ArgumentNullException(nameof(runtimeConfiguration));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _productApiCredentialStore = productApiCredentialStore ?? throw new ArgumentNullException(nameof(productApiCredentialStore));
        _machineIdentityProvider = machineIdentityProvider ?? throw new ArgumentNullException(nameof(machineIdentityProvider));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Current = InitialState();
    }

    public LicenseRuntimeService(
        LicenseHubRuntimeConfiguration runtimeConfiguration,
        ILicenseCredentialStore credentialStore,
        IMachineIdentityProvider machineIdentityProvider,
        HttpClient httpClient)
        : this(
            runtimeConfiguration,
            credentialStore,
            new ConfigurationProductApiCredentialStore(runtimeConfiguration?.ProductApiKey),
            machineIdentityProvider,
            httpClient)
    {
    }

    public LicenseRuntimeSnapshot Current { get; private set; }

    public async Task<LicenseRuntimeSnapshot> InitializeAsync(
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        var client = await TryGetClientAsync(cancellationToken);
        if (client is null)
        {
            Current = _runtimeConfiguration.Required
                ? ConfigurationFailure("LicenseHub ist für diesen Build erforderlich, aber die lokale Produktkonfiguration fehlt.")
                : DevelopmentBypass();
            return Current;
        }

        return await RefreshWithClientAsync(client, appVersion, cancellationToken);
    }

    public async Task<LicenseRuntimeSnapshot> ActivateAsync(
        string licenseKey,
        string deviceName,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        var client = await RequireClientAsync(cancellationToken);
        var machineId = await _machineIdentityProvider.GetMachineIdAsync(cancellationToken);
        var result = await client.ActivateAsync(licenseKey, machineId, deviceName, appVersion, cancellationToken);
        await ForgetRejectedProductCredentialAsync(result, cancellationToken);
        Current = Map(result);

        if (result.AllowsUse)
        {
            await _credentialStore.SaveLicenseKeyAsync(licenseKey, cancellationToken);
        }

        return Current;
    }

    public async Task<LicenseRuntimeSnapshot> RefreshAsync(
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        var client = await TryGetClientAsync(cancellationToken);
        if (client is null)
        {
            Current = _runtimeConfiguration.Required
                ? ConfigurationFailure("LicenseHub ist für diesen Build erforderlich, aber die lokale Produktkonfiguration fehlt.")
                : DevelopmentBypass();
            return Current;
        }

        return await RefreshWithClientAsync(client, appVersion, cancellationToken);
    }

    public async Task<LicenseRuntimeSnapshot> DeactivateAsync(CancellationToken cancellationToken = default)
    {
        var client = await TryGetClientAsync(cancellationToken);
        if (client is null)
        {
            if (!_runtimeConfiguration.Required)
            {
                await _credentialStore.ClearLicenseKeyAsync(cancellationToken);
                Current = DevelopmentBypass();
                return Current;
            }

            Current = ConfigurationFailure("LicenseHub ist für diesen Build erforderlich, aber die lokale Produktkonfiguration fehlt.");
            return Current;
        }

        var licenseKey = await _credentialStore.LoadLicenseKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            Current = MissingLicense();
            return Current;
        }

        var machineId = await _machineIdentityProvider.GetMachineIdAsync(cancellationToken);
        await client.DeactivateAsync(licenseKey, machineId, cancellationToken);
        await _credentialStore.ClearLicenseKeyAsync(cancellationToken);
        Current = MissingLicense();
        return Current;
    }

    private async Task<LicenseRuntimeSnapshot> RefreshWithClientAsync(
        ILicenseHubLicenseClient client,
        string appVersion,
        CancellationToken cancellationToken)
    {
        var licenseKey = await _credentialStore.LoadLicenseKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            Current = MissingLicense();
            return Current;
        }

        var machineId = await _machineIdentityProvider.GetMachineIdAsync(cancellationToken);
        var result = await client.ValidateAsync(licenseKey, machineId, appVersion, cancellationToken);
        await ForgetRejectedProductCredentialAsync(result, cancellationToken);
        Current = Map(result);
        return Current;
    }

    private async Task ForgetRejectedProductCredentialAsync(
        LicenseAccessSnapshot result,
        CancellationToken cancellationToken)
    {
        if (result.State != LicenseAccessState.InvalidProductCredentials) return;

        _licenseClient = null;
        try
        {
            await _productApiCredentialStore.ClearProductApiKeyAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // A rejected product credential must never stay cached in-memory.
            // Persistent cleanup is best effort; a replacement value can still overwrite it.
        }
    }

    private async Task<ILicenseHubLicenseClient?> TryGetClientAsync(CancellationToken cancellationToken)
    {
        if (_licenseClient is not null) return _licenseClient;
        if (!_runtimeConfiguration.HasEndpointConfiguration) return null;

        string? apiKey;
        try
        {
            apiKey = _runtimeConfiguration.ProductApiKey?.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = await _productApiCredentialStore.LoadProductApiKeyAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(apiKey)) return null;

        try
        {
            _licenseClient = new LicenseHubHttpClient(
                _httpClient,
                _runtimeConfiguration.CreateClientConfiguration(apiKey));
            return _licenseClient;
        }
        catch (LicenseHubConfigurationException)
        {
            return null;
        }
    }

    private async Task<ILicenseHubLicenseClient> RequireClientAsync(CancellationToken cancellationToken)
    {
        var client = await TryGetClientAsync(cancellationToken);
        if (client is not null) return client;
        throw new LicenseHubConfigurationException(
            _runtimeConfiguration.Required
                ? "LicenseHub is required but the local product credential is not configured."
                : "LicenseHub is not configured for this development build.");
    }

    private LicenseRuntimeSnapshot InitialState() =>
        _runtimeConfiguration.Required
            ? ConfigurationFailure("LicenseHub-Lizenzstatus wurde noch nicht geprüft.")
            : DevelopmentBypass();

    private LicenseRuntimeSnapshot Map(LicenseAccessSnapshot source)
    {
        var state = source.State switch
        {
            LicenseAccessState.Active => LicenseRuntimeState.Active,
            LicenseAccessState.Blocked => LicenseRuntimeState.Blocked,
            LicenseAccessState.Expired => LicenseRuntimeState.Expired,
            LicenseAccessState.NotActivated => LicenseRuntimeState.NotActivated,
            LicenseAccessState.ActivationLimitReached => LicenseRuntimeState.ActivationLimitReached,
            LicenseAccessState.LicenseNotFound => LicenseRuntimeState.LicenseNotFound,
            LicenseAccessState.InvalidProductCredentials => LicenseRuntimeState.InvalidProductCredentials,
            LicenseAccessState.Unavailable => LicenseRuntimeState.ServiceUnavailable,
            _ => LicenseRuntimeState.ProtocolError
        };

        return new LicenseRuntimeSnapshot(
            state,
            _runtimeConfiguration.Required,
            TranslateMessage(state, source.Message),
            source.ExpiresAt,
            source.MaxActivations,
            source.CurrentActivations,
            source.CustomerName);
    }

    private LicenseRuntimeSnapshot DevelopmentBypass() =>
        new(
            LicenseRuntimeState.DevelopmentBypass,
            false,
            _runtimeConfiguration.HasAnyConfiguration
                ? "LicenseHub ist nur teilweise konfiguriert; Lizenzdurchsetzung ist in diesem Entwicklungsbuild deaktiviert."
                : "LicenseHub ist in diesem Entwicklungsbuild noch nicht aktiviert.");

    private LicenseRuntimeSnapshot MissingLicense() =>
        new(
            LicenseRuntimeState.LicenseMissing,
            _runtimeConfiguration.Required,
            _runtimeConfiguration.Required
                ? "Für NMC SCS LAUNCHER ist noch keine Lizenz aktiviert."
                : "Keine LicenseHub-Lizenz gespeichert; Lizenzdurchsetzung ist für diesen Entwicklungsbuild deaktiviert.");

    private LicenseRuntimeSnapshot ConfigurationFailure(string message) =>
        new(LicenseRuntimeState.ConfigurationError, _runtimeConfiguration.Required, message);

    private static string TranslateMessage(LicenseRuntimeState state, string fallback) => state switch
    {
        LicenseRuntimeState.Active => "LicenseHub-Lizenz ist aktiv.",
        LicenseRuntimeState.Blocked => "Die LicenseHub-Lizenz wurde gesperrt.",
        LicenseRuntimeState.Expired => "Die LicenseHub-Lizenz ist abgelaufen.",
        LicenseRuntimeState.NotActivated => "Dieser Computer ist für die LicenseHub-Lizenz nicht aktiviert.",
        LicenseRuntimeState.ActivationLimitReached => "Das Aktivierungslimit der LicenseHub-Lizenz wurde erreicht.",
        LicenseRuntimeState.LicenseNotFound => "Die LicenseHub-Lizenz wurde nicht gefunden.",
        LicenseRuntimeState.InvalidProductCredentials => "Die LicenseHub-Produktkonfiguration wurde vom Server abgewiesen.",
        LicenseRuntimeState.ServiceUnavailable => "LicenseHub ist derzeit nicht erreichbar; die Lizenz konnte nicht bestätigt werden.",
        LicenseRuntimeState.ProtocolError => "Die LicenseHub-Antwort konnte nicht sicher ausgewertet werden.",
        _ => fallback
    };

    private sealed class ConfigurationProductApiCredentialStore : IProductApiCredentialStore
    {
        private readonly string? _value;

        public ConfigurationProductApiCredentialStore(string? value)
        {
            _value = value?.Trim();
        }

        public Task<string?> LoadProductApiKeyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(string.IsNullOrWhiteSpace(_value) ? null : _value);
        }

        public Task SaveProductApiKeyAsync(string productApiKey, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task ClearProductApiKeyAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
