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
            await File.WriteAllBytesAsync(Path.Combine(modDirectory, "map.scs"), [1, 2, 3, 4]);
            await File.WriteAllBytesAsync(Path.Combine(modDirectory, "truck.SCS"), [5, 6]);
            await File.WriteAllTextAsync(Path.Combine(modDirectory, "notes.txt"), "not a mod");
            var unpackedOne = Directory.CreateDirectory(Path.Combine(modDirectory, "unpacked-one")).FullName;
            Directory.CreateDirectory(Path.Combine(modDirectory, "unpacked-two"));
            await File.WriteAllBytesAsync(Path.Combine(unpackedOne, "payload.bin"), [1, 2, 3]);

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
            Assert.Equal(4, inspection.LocalMods.Count);
            Assert.DoesNotContain(inspection.LocalMods, static mod => mod.Name == "notes.txt");

            var map = Assert.Single(inspection.LocalMods, static mod => mod.Name == "map.scs");
            Assert.Equal(LocalModKind.ScsPackage, map.Kind);
            Assert.Equal(4, map.SizeBytes);
            Assert.Equal(Path.GetFullPath(Path.Combine(modDirectory, "map.scs")), map.Path);

            var unpacked = Assert.Single(inspection.LocalMods, static mod => mod.Name == "unpacked-one");
            Assert.Equal(LocalModKind.ExtractedDirectory, unpacked.Kind);
            Assert.Equal(3, unpacked.SizeBytes);
            Assert.Equal(Path.GetFullPath(unpackedOne), unpacked.Path);

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
    public async Task DirectModsetReadsExactlySelectedModFolder()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var runtimeHome = Directory.CreateDirectory(Path.Combine(root, "runtime")).FullName;
            var modDirectory = Directory.CreateDirectory(Path.Combine(root, "mods-exact")).FullName;
            await File.WriteAllTextAsync(Path.Combine(modDirectory, "map.scs"), string.Empty);
            Directory.CreateDirectory(Path.Combine(modDirectory, "unpacked"));

            var modset = CreateModset(GameType.Ets2, runtimeHome) with
            {
                ModDirectoryPath = modDirectory
            };

            var inspection = await new ScsModsetInspector().InspectAsync(modset);

            Assert.True(inspection.GameDataDirectoryExists);
            Assert.Equal(Path.GetFullPath(modDirectory), inspection.ModDirectory);
            Assert.Equal(1, inspection.PackageModCount);
            Assert.Equal(1, inspection.ExtractedModCount);
            Assert.Equal(2, inspection.TotalModCount);
            Assert.Equal(2, inspection.LocalMods.Count);
            Assert.Contains(inspection.LocalMods, static mod => mod.Name == "map.scs" && mod.Kind == LocalModKind.ScsPackage);
            Assert.Contains(inspection.LocalMods, static mod => mod.Name == "unpacked" && mod.Kind == LocalModKind.ExtractedDirectory);
            Assert.False(Directory.Exists(Path.Combine(modDirectory, "Euro Truck Simulator 2")));
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
            Assert.Empty(inspection.LocalMods);
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
