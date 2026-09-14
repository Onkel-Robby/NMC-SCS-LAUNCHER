using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class LicensedGameLaunchServiceTests
{
    [Fact]
    public async Task PrepareAddsBlockingLicenseCheckWhenLicenseIsNotActive()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        var installRoot = Path.Combine(root, "game");
        var definition = GameDefinition.For(GameType.Ets2);
        var executable = Path.Combine(installRoot, definition.ExecutableRelativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        await File.WriteAllBytesAsync(executable, []);

        var home = Path.Combine(root, "home");
        Directory.CreateDirectory(home);
        var modset = CreateModset(GameType.Ets2, home);
        var installation = new GameInstallation(GameType.Ets2, installRoot, executable, GameInstallationSource.ManualSelection);
        var runtime = new FixedLicenseRuntime(new LicenseRuntimeSnapshot(
            LicenseRuntimeState.Expired,
            true,
            "Die LicenseHub-Lizenz ist abgelaufen."));
        var service = new LicensedGameLaunchService(new ScsGameLaunchService(), runtime);

        var plan = await service.PrepareAsync(modset, installation);

        var licenseCheck = Assert.Single(plan.Checks, check => check.Title == "LicenseHub");
        Assert.Equal(LaunchCheckSeverity.Error, licenseCheck.Severity);
        Assert.False(plan.CanLaunch);
    }

    [Fact]
    public async Task LaunchRefusesStalePlanWhenLicenseBecomesInvalid()
    {
        var runtime = new FixedLicenseRuntime(new LicenseRuntimeSnapshot(
            LicenseRuntimeState.Blocked,
            true,
            "Die LicenseHub-Lizenz wurde gesperrt."));
        var service = new LicensedGameLaunchService(new ScsGameLaunchService(), runtime);
        var modset = CreateModset(GameType.Ats, Path.GetTempPath());
        var installation = new GameInstallation(GameType.Ats, Path.GetTempPath(), "unused.exe", GameInstallationSource.ManualSelection);
        var plan = new GameLaunchPlan(modset, installation, Path.GetTempPath(), Array.Empty<string>(), Array.Empty<LaunchCheckItem>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LaunchAsync(plan));

        Assert.Contains("LicenseHub", exception.Message, StringComparison.Ordinal);
    }

    private static Modset CreateModset(GameType game, string homeBasePath)
    {
        var now = DateTimeOffset.UtcNow;
        return new Modset
        {
            Id = Guid.NewGuid(),
            Game = game,
            Name = "Test",
            Description = null,
            HomeBasePath = homeBasePath,
            CreatedAt = now,
            UpdatedAt = now,
            LastStartedAt = null,
            PreferredProfile = null,
            AdditionalLaunchArguments = null,
            IsManagedDirectory = true
        };
    }

    private sealed class FixedLicenseRuntime : ILicenseRuntimeService
    {
        public FixedLicenseRuntime(LicenseRuntimeSnapshot current)
        {
            Current = current;
        }

        public LicenseRuntimeSnapshot Current { get; private set; }

        public Task<LicenseRuntimeSnapshot> InitializeAsync(string appVersion, CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task<LicenseRuntimeSnapshot> ActivateAsync(string licenseKey, string deviceName, string appVersion, CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task<LicenseRuntimeSnapshot> RefreshAsync(string appVersion, CancellationToken cancellationToken = default) => Task.FromResult(Current);
        public Task<LicenseRuntimeSnapshot> DeactivateAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current);
    }
}
