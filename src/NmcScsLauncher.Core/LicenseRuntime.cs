namespace NmcScsLauncher.Core;

public enum LicenseRuntimeState
{
    DevelopmentBypass,
    LicenseMissing,
    Active,
    Blocked,
    Expired,
    NotActivated,
    ActivationLimitReached,
    LicenseNotFound,
    InvalidProductCredentials,
    ServiceUnavailable,
    ConfigurationError,
    ProtocolError
}

public sealed record LicenseRuntimeSnapshot(
    LicenseRuntimeState State,
    bool EnforcementEnabled,
    string Message,
    DateTimeOffset? ExpiresAt = null,
    int? MaxActivations = null,
    int? CurrentActivations = null,
    string? CustomerName = null)
{
    public bool AllowsUse => !EnforcementEnabled || State == LicenseRuntimeState.Active;

    public bool HasStoredLicense => State is not LicenseRuntimeState.DevelopmentBypass
        and not LicenseRuntimeState.LicenseMissing
        and not LicenseRuntimeState.ConfigurationError;
}

public interface ILicenseRuntimeService
{
    LicenseRuntimeSnapshot Current { get; }

    Task<LicenseRuntimeSnapshot> InitializeAsync(
        string appVersion,
        CancellationToken cancellationToken = default);

    Task<LicenseRuntimeSnapshot> ActivateAsync(
        string licenseKey,
        string deviceName,
        string appVersion,
        CancellationToken cancellationToken = default);

    Task<LicenseRuntimeSnapshot> RefreshAsync(
        string appVersion,
        CancellationToken cancellationToken = default);

    Task<LicenseRuntimeSnapshot> DeactivateAsync(
        CancellationToken cancellationToken = default);
}
