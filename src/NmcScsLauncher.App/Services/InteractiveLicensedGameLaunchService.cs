using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;

namespace NmcScsLauncher.App.Services;

public sealed class InteractiveLicensedGameLaunchService : IGameLaunchService
{
    private readonly LicensedGameLaunchService _inner;
    private readonly ILicenseRuntimeService _licenseRuntime;
    private readonly ILicenseActivationDialogService _activationDialog;

    public InteractiveLicensedGameLaunchService(
        LicensedGameLaunchService inner,
        ILicenseRuntimeService licenseRuntime,
        ILicenseActivationDialogService activationDialog)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _licenseRuntime = licenseRuntime ?? throw new ArgumentNullException(nameof(licenseRuntime));
        _activationDialog = activationDialog ?? throw new ArgumentNullException(nameof(activationDialog));
    }

    public async Task<GameLaunchPlan> PrepareAsync(
        Modset modset,
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var state = _licenseRuntime.Current;
        if (state.EnforcementEnabled && !state.AllowsUse)
        {
            _ = _activationDialog.Show(_licenseRuntime, state, AppVersionInfo.Current);
        }

        return await _inner.PrepareAsync(modset, installation, cancellationToken);
    }

    public Task<int> LaunchAsync(GameLaunchPlan plan, CancellationToken cancellationToken = default) =>
        _inner.LaunchAsync(plan, cancellationToken);
}
