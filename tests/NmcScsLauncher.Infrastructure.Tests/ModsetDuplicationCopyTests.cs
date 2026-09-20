using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ModsetDuplicationCopyTests
{
    [Fact]
    public async Task DuplicateCopiesSelectedConfigurationModsAndProfiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var manager = new ModsetManager(new JsonModsetStore(Path.Combine(root, "modsets.json")));
        var sourceHome = Path.Combine(root, "source");
        var source = await manager.CreateAsync(new ModsetDraft(GameType.Ets2, "Source", "Beschreibung", sourceHome, "Profile A"));
        var gameRoot = Path.Combine(sourceHome, GameDefinition.For(GameType.Ets2).HomeDirectoryName);
        Directory.CreateDirectory(Path.Combine(gameRoot, "mod"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "profiles", "profile-a"));
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "config.cfg"), "config");
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "mod", "map.scs"), "mod");
        await File.WriteAllTextAsync(Path.Combine(gameRoot, "profiles", "profile-a", "profile.sii"), "profile");

        var targetHome = Path.Combine(root, "target");
        var service = new ModsetDuplicationService(manager);
        var result = await service.DuplicateAsync(new ModsetDuplicationRequest(
            source.Id,
            "Copy",
            targetHome,
            ModsetCopyContent.Configuration | ModsetCopyContent.Mods | ModsetCopyContent.Profiles));

        var targetGameRoot = Path.Combine(targetHome, GameDefinition.For(GameType.Ets2).HomeDirectoryName);
        Assert.True(File.Exists(Path.Combine(targetGameRoot, "config.cfg")));
        Assert.True(File.Exists(Path.Combine(targetGameRoot, "mod", "map.scs")));
        Assert.True(File.Exists(Path.Combine(targetGameRoot, "profiles", "profile-a", "profile.sii")));
        Assert.NotEqual(source.Id, result.Modset.Id);
        Assert.Equal(source.Description, result.Modset.Description);
        Assert.Equal(source.PreferredProfile, result.Modset.PreferredProfile);
        Assert.True(result.Modset.IsManagedDirectory);
        Assert.Equal(2, (await manager.GetAllAsync()).Count);
    }

    [Fact]
    public async Task DuplicateDirectModsetCopiesFilesIntoExactTargetFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Modset? source = null;
        Modset? copy = null;
        try
        {
            using var manager = new ModsetManager(new JsonModsetStore(Path.Combine(root, "modsets.json")));
            var sourceMods = Directory.CreateDirectory(Path.Combine(root, "source-mods")).FullName;
            await File.WriteAllTextAsync(Path.Combine(sourceMods, "map.scs"), "mod");

            source = await manager.ImportAsync(new ModsetDraft(
                GameType.Ets2,
                "Direct Source",
                null,
                string.Empty,
                null,
                null,
                sourceMods));

            var targetMods = Path.Combine(root, "copy-mods");
            var result = await new ModsetDuplicationService(manager).DuplicateAsync(
                new ModsetDuplicationRequest(
                    source.Id,
                    "Direct Copy",
                    targetMods,
                    ModsetCopyContent.Mods));
            copy = result.Modset;

            Assert.Equal(Path.GetFullPath(targetMods), copy.ModDirectoryPath);
            Assert.True(File.Exists(Path.Combine(targetMods, "map.scs")));
            Assert.False(Directory.Exists(Path.Combine(targetMods, "Euro Truck Simulator 2")));
        }
        finally
        {
            if (source is not null && Directory.Exists(source.HomeBasePath))
                Directory.Delete(source.HomeBasePath, recursive: true);
            if (copy is not null && Directory.Exists(copy.HomeBasePath))
                Directory.Delete(copy.HomeBasePath, recursive: true);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
