using System.IO.Compression;
using System.Text.Json;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ModsetBackupServiceTests
{
    [Fact]
    public async Task BackupCreatesArchiveWithConfigurationProfilesAndManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var manager = new ModsetManager(new JsonModsetStore(Path.Combine(root, "modsets.json")));
        var home = Path.Combine(root, "home");
        var modset = await manager.CreateAsync(new ModsetDraft(GameType.Ets2, "Backup Test", null, home));
        var gameRoot = Path.Combine(home, GameDefinition.For(GameType.Ets2).HomeDirectoryName);
        Directory.CreateDirectory(Path.Combine(gameRoot, "profiles", "profile-a"));
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "config.cfg"), "uset test 1");
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "profiles", "profile-a", "profile.sii"), "profile");

        var archivePath = Path.Combine(root, "backup.zip");
        var service = new ModsetBackupService(manager);
        var result = await service.CreateAsync(new ModsetBackupRequest(
            modset.Id,
            archivePath,
            ModsetBackupContent.Configuration | ModsetBackupContent.Profiles));

        Assert.True(File.Exists(archivePath));
        Assert.Equal(2, result.FilesArchived);
        Assert.Equal(modset.Id, result.Manifest.ModsetId);
        Assert.Equal(GameType.Ets2, result.Manifest.Game);

        using var archive = ZipFile.OpenRead(archivePath);
        Assert.NotNull(archive.GetEntry("data/config.cfg"));
        Assert.NotNull(archive.GetEntry("data/profiles/profile-a/profile.sii"));
        var manifestEntry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry("nmc-backup.json"));
        await using var manifestStream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<ModsetBackupManifest>(manifestStream);
        Assert.NotNull(manifest);
        Assert.Equal("NMC SCS LAUNCHER", manifest.Product);
        Assert.Equal("Backup Test", manifest.ModsetName);
        Assert.Equal(2, manifest.Files.Count);
    }

    [Fact]
    public async Task DirectModsetBackupUsesExactSelectedModFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Modset? modset = null;
        try
        {
            using var manager = new ModsetManager(new JsonModsetStore(Path.Combine(root, "modsets.json")));
            var modDirectory = Directory.CreateDirectory(Path.Combine(root, "exact-mod-folder")).FullName;
            await File.WriteAllTextAsync(Path.Combine(modDirectory, "map.scs"), "mod-package");

            modset = await manager.CreateAsync(new ModsetDraft(
                GameType.Ets2,
                "Direct Backup",
                null,
                string.Empty,
                null,
                null,
                modDirectory));

            var archivePath = Path.Combine(root, "direct-mods.zip");
            var result = await new ModsetBackupService(manager).CreateAsync(
                new ModsetBackupRequest(modset.Id, archivePath, ModsetBackupContent.Mods));

            Assert.Equal(1, result.FilesArchived);
            using var archive = ZipFile.OpenRead(archivePath);
            Assert.NotNull(archive.GetEntry("data/mod/map.scs"));
            Assert.Null(archive.GetEntry("data/Euro Truck Simulator 2/mod/map.scs"));
        }
        finally
        {
            if (modset is not null && Directory.Exists(modset.HomeBasePath))
            {
                Directory.Delete(modset.HomeBasePath, recursive: true);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task BackupCanIncludeModsExplicitly()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var manager = new ModsetManager(new JsonModsetStore(Path.Combine(root, "modsets.json")));
        var home = Path.Combine(root, "home");
        var modset = await manager.CreateAsync(new ModsetDraft(GameType.Ats, "ATS Backup", null, home));
        var gameRoot = Path.Combine(home, GameDefinition.For(GameType.Ats).HomeDirectoryName);
        Directory.CreateDirectory(Path.Combine(gameRoot, "mod"));
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "mod", "map.scs"), "mod-package");

        var archivePath = Path.Combine(root, "mods-backup.zip");
        var service = new ModsetBackupService(manager);
        var result = await service.CreateAsync(new ModsetBackupRequest(modset.Id, archivePath, ModsetBackupContent.Mods));

        Assert.Equal(1, result.FilesArchived);
        using var archive = ZipFile.OpenRead(archivePath);
        Assert.NotNull(archive.GetEntry("data/mod/map.scs"));
        Assert.NotNull(archive.GetEntry("nmc-backup.json"));
    }
}
