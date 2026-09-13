using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsModsetInspectorTests
{
    [Fact]
    public async Task CountsScsPackagesExtractedModsAndProfilesWithoutWritingSiiFiles()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var homeBase = Directory.CreateDirectory(Path.Combine(root, "MapCombo")).FullName;
            var gameData = Directory.CreateDirectory(Path.Combine(homeBase, "Euro Truck Simulator 2")).FullName;
            var modDirectory = Directory.CreateDirectory(Path.Combine(gameData, "mod")).FullName;
            await File.WriteAllTextAsync(Path.Combine(modDirectory, "map.scs"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(modDirectory, "truck.SCS"), string.Empty);
            await File.WriteAllTextAsync(Path.Combine(modDirectory, "notes.txt"), string.Empty);
            Directory.CreateDirectory(Path.Combine(modDirectory, "unpacked-one"));
            Directory.CreateDirectory(Path.Combine(modDirectory, "unpacked-two"));

            var profiles = Directory.CreateDirectory(Path.Combine(gameData, "profiles")).FullName;
            Directory.CreateDirectory(Path.Combine(profiles, "local-a"));
            var steamProfiles = Directory.CreateDirectory(Path.Combine(gameData, "steam_profiles")).FullName;
            Directory.CreateDirectory(Path.Combine(steamProfiles, "steam-a"));
            Directory.CreateDirectory(Path.Combine(steamProfiles, "steam-b"));

            var inspection = await new ScsModsetInspector().InspectAsync(CreateModset(GameType.Ets2, homeBase));

            Assert.True(inspection.GameDataDirectoryExists);
            Assert.Equal(2, inspection.PackageModCount);
            Assert.Equal(2, inspection.ExtractedModCount);
            Assert.Equal(4, inspection.TotalModCount);
            Assert.Equal(1, inspection.LocalProfileCount);
            Assert.Equal(2, inspection.SteamProfileCount);
            Assert.Equal(3, inspection.TotalProfileCount);
            Assert.Empty(inspection.Warnings);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MissingGameDataDirectoryReturnsEmptyInspectionInsteadOfCreatingIt()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var inspection = await new ScsModsetInspector().InspectAsync(CreateModset(GameType.Ats, root));

            Assert.False(inspection.GameDataDirectoryExists);
            Assert.Equal(0, inspection.TotalModCount);
            Assert.Empty(inspection.Profiles);
            Assert.NotEmpty(inspection.Warnings);
            Assert.False(Directory.Exists(Path.Combine(root, "American Truck Simulator")));
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
        Name = "Inspection Test",
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
