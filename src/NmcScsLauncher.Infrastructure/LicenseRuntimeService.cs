using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class LicenseRuntimeService : ILicenseRuntimeService
{
    private readonly LicenseHubRuntimeConfiguration _runtimeConfiguration;
    private readonly ILicenseCredentialStore _credentialStore;
    private readonly IMachineIdentityProvider _machineIdentityProvider;
    private readonly ILicenseHubLicenseClient? _licenseClient;

    public LicenseRuntimeService(
        LicenseHubRuntimeConfiguration runtimeConfiguration,
        ILicenseCredentialStore credentialStore,
        IMachineIdentityProvider machineIdentityProvider,
        HttpClient httpClient)
    {
        _runtimeConfiguration = runtimeConfiguration ?? throw new ArgumentNullException(nameof(runtimeConfiguration));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _machineIdentityProvider = machineIdentityProvider ?? throw new ArgumentNullException(nameof(machineIdentityProvider));
        ArgumentNullException.ThrowIfNull(httpClient);

        if (_runtimeConfiguration.HasLicenseConfiguration)
        {
            _licenseClient = new LicenseHubHttpClient(httpClient, _runtimeConfiguration.CreateClientConfiguration());
        }

        Current = InitialState();
    }

    public LicenseRuntimeSnapshot Current { get; private set; }

    public async Task<LicenseRuntimeSnapshot> InitializeAsync(
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        if (_licenseClient is null)
        {
            Current = _runtimeConfiguration.Required
                ? ConfigurationFailure("LicenseHub ist für diesen Build erforderlich, aber nicht vollständig konfiguriert.")
                : DevelopmentBypass();
            return Current;
        }

        return await RefreshAsync(appVersion, cancellationToken);
    }

    public async Task<LicenseRuntimeSnapshot> ActivateAsync(
        string licenseKey,
        string deviceName,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        var client = RequireClient();
        var machineId = await _machineIdentityProvider.GetMachineIdAsync(cancellationToken);
        var result = await client.ActivateAsync(licenseKey, machineId, deviceName, appVersion, cancellationToken);
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
        if (_licenseClient is null)
        {
            Current = _runtimeConfiguration.Required
                ? ConfigurationFailure("LicenseHub ist für diesen Build erforderlich, aber nicht vollständig konfiguriert.")
                : DevelopmentBypass();
            return Current;
        }

        var licenseKey = await _credentialStore.LoadLicenseKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            Current = MissingLicense();
            return Current;
        }

        var machineId = await _machineIdentityProvider.GetMachineIdAsync(cancellationToken);
        var result = await _licenseClient.ValidateAsync(licenseKey, machineId, appVersion, cancellationToken);
        Current = Map(result);
        return Current;
    }

    public async Task<LicenseRuntimeSnapshot> DeactivateAsync(CancellationToken cancellationToken = default)
    {
        if (_licenseClient is null)
        {
            if (!_runtimeConfiguration.Required)
            {
                await _credentialStore.ClearLicenseKeyAsync(cancellationToken);
                Current = DevelopmentBypass();
                return Current;
            }

            Current = ConfigurationFailure("LicenseHub ist für diesen Build erforderlich, aber nicht vollständig konfiguriert.");
            return Current;
        }

        var licenseKey = await _credentialStore.LoadLicenseKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            Current = MissingLicense();
            return Current;
        }

        var machineId = await _machineIdentityProvider.GetMachineIdAsync(cancellationToken);
        await _licenseClient.DeactivateAsync(licenseKey, machineId, cancellationToken);
        await _credentialStore.ClearLicenseKeyAsync(cancellationToken);
        Current = MissingLicense();
        return Current;
    }

    private ILicenseHubLicenseClient RequireClient()
    {
        if (_licenseClient is not null) return _licenseClient;
        throw new LicenseHubConfigurationException(
            _runtimeConfiguration.Required
                ? "LicenseHub is required but not configured."
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
}
