using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class LicensedGameLaunchService : IGameLaunchService
{
    private readonly ScsGameLaunchService _inner;
    private readonly ILicenseRuntimeService _licenseRuntime;

    public LicensedGameLaunchService(
        ScsGameLaunchService inner,
        ILicenseRuntimeService licenseRuntime)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _licenseRuntime = licenseRuntime ?? throw new ArgumentNullException(nameof(licenseRuntime));
    }

    public async Task<GameLaunchPlan> PrepareAsync(
        Modset modset,
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        var plan = await _inner.PrepareAsync(modset, installation, cancellationToken);
        var license = _licenseRuntime.Current;
        var checks = plan.Checks.ToList();

        checks.Insert(0, license.AllowsUse
            ? new LaunchCheckItem(
                "LicenseHub",
                license.EnforcementEnabled
                    ? "Lizenz serverseitig bestätigt."
                    : "Lizenzdurchsetzung ist für diesen Entwicklungsbuild deaktiviert.",
                license.EnforcementEnabled ? LaunchCheckSeverity.Success : LaunchCheckSeverity.Warning)
            : new LaunchCheckItem(
                "LicenseHub",
                license.Message,
                LaunchCheckSeverity.Error));

        return plan with { Checks = checks };
    }

    public Task<int> LaunchAsync(GameLaunchPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        var license = _licenseRuntime.Current;
        if (!license.AllowsUse)
        {
            throw new InvalidOperationException(
                $"LicenseHub blockiert den Spielstart: {license.Message}");
        }

        return _inner.LaunchAsync(plan, cancellationToken);
    }
}
