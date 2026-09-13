using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsGameLaunchServiceTests
{
    [Fact]
    public async Task PrepareUsesHomeBaseAboveScsGameFolder()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var install = Path.Combine(root, "install");
            var executable = Path.Combine(install, "bin", "win_x64", "eurotrucks2.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            await File.WriteAllTextAsync(executable, string.Empty);
            var homeBase = Directory.CreateDirectory(Path.Combine(root, "MapComboHome")).FullName;
            var modset = CreateModset(GameType.Ets2, homeBase);
            var installation = new GameInstallation(GameType.Ets2, install, executable, GameInstallationSource.ManualSelection);

            var plan = await new ScsGameLaunchService().PrepareAsync(modset, installation);

            Assert.True(plan.CanLaunch);
            Assert.Equal(Path.Combine(homeBase, "Euro Truck Simulator 2"), plan.GameDataDirectory);
            Assert.Contains(plan.Checks, check => check.Title == "SCS-Datenordner" && check.Severity == LaunchCheckSeverity.Warning);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareBlocksHomeBaseThatPointsAtGameFolderItself()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var install = Path.Combine(root, "install");
            var executable = Path.Combine(install, "bin", "win_x64", "amtrucks.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            await File.WriteAllTextAsync(executable, string.Empty);
            var wrongHome = Directory.CreateDirectory(Path.Combine(root, "American Truck Simulator")).FullName;

            var plan = await new ScsGameLaunchService().PrepareAsync(
                CreateModset(GameType.Ats, wrongHome),
                new GameInstallation(GameType.Ats, install, executable, GameInstallationSource.ManualSelection));

            Assert.False(plan.CanLaunch);
            Assert.Contains(plan.Checks, check => check.Title == "Home-Basis" && check.Severity == LaunchCheckSeverity.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareBlocksUserSuppliedHomeDirArgument()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var install = Path.Combine(root, "install");
            var executable = Path.Combine(install, "bin", "win_x64", "eurotrucks2.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            await File.WriteAllTextAsync(executable, string.Empty);
            var homeBase = Directory.CreateDirectory(Path.Combine(root, "home")).FullName;
            var modset = CreateModset(GameType.Ets2, homeBase) with { AdditionalLaunchArguments = "-nointro -homedir D:\\Other" };

            var plan = await new ScsGameLaunchService().PrepareAsync(
                modset,
                new GameInstallation(GameType.Ets2, install, executable, GameInstallationSource.ManualSelection));

            Assert.False(plan.CanLaunch);
            Assert.Contains(plan.Checks, check => check.Title == "Startparameter" && check.Severity == LaunchCheckSeverity.Error);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static Modset CreateModset(GameType game, string homeBasePath) => new()
    {
        Id = Guid.NewGuid(),
        Game = game,
        Name = "Test",
        HomeBasePath = homeBasePath,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        IsManagedDirectory = true
    };

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
