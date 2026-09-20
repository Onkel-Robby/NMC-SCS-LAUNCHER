using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ModsetManagerTests
{
    [Fact]
    public async Task CreateCreatesManagedHomeAndPersistsEntry()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var manager = new ModsetManager(new JsonModsetStore(Path.Combine(root, "modsets.json")));
            var home = Path.Combine(root, "homes", "MapCombo");
            var created = await manager.CreateAsync(new ModsetDraft(GameType.Ets2, "Map Combo", null, home));
            var stored = await manager.GetAllAsync();
            Assert.True(Directory.Exists(home));
            Assert.True(created.IsManagedDirectory);
            Assert.Equal(created.Id, Assert.Single(stored).Id);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DirectModsetUsesSelectedFolderWithoutAppendingGameDirectory()
    {
        var root = CreateTemporaryDirectory();
        Modset? created = null;
        try
        {
            using var manager = new ModsetManager(new JsonModsetStore(Path.Combine(root, "modsets.json")));
            var modDirectory = Path.Combine(root, "My Exact Mods");

            created = await manager.CreateAsync(new ModsetDraft(
                GameType.Ets2,
                "Direct Mods",
                null,
                string.Empty,
                null,
                null,
                modDirectory));

            Assert.Equal(Path.GetFullPath(modDirectory), created.ModDirectoryPath);
            Assert.Equal(Path.GetFullPath(modDirectory), created.ModFolderDisplayPath);
            Assert.True(Directory.Exists(modDirectory));
            Assert.NotEqual(Path.GetFullPath(modDirectory), Path.GetFullPath(created.HomeBasePath));
            Assert.DoesNotContain(
                GameDefinition.For(GameType.Ets2).HomeDirectoryName,
                Path.GetRelativePath(root, modDirectory),
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (created is not null && Directory.Exists(created.HomeBasePath))
            {
                Directory.Delete(created.HomeBasePath, recursive: true);
            }

            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RemovingImportedModsetNeverDeletesImportedDirectory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var importedHome = Directory.CreateDirectory(Path.Combine(root, "existing-home")).FullName;
            using var manager = new ModsetManager(new JsonModsetStore(Path.Combine(root, "modsets.json")));
            var imported = await manager.ImportAsync(new ModsetDraft(GameType.Ats, "Existing ATS", null, importedHome));
            await manager.RemoveAsync(imported.Id);
            Assert.True(Directory.Exists(importedHome));
            Assert.Empty(await manager.GetAllAsync());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DuplicateNameForSameGameIsRejected()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var manager = new ModsetManager(new JsonModsetStore(Path.Combine(root, "modsets.json")));
            await manager.CreateAsync(new ModsetDraft(GameType.Ets2, "ProMods", null, Path.Combine(root, "one")));
            await Assert.ThrowsAsync<ModsetValidationException>(() => manager.CreateAsync(new ModsetDraft(GameType.Ets2, "promods", null, Path.Combine(root, "two"))));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task MarkStartedPersistsTimestampWithoutChangingIdentity()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var manager = new ModsetManager(new JsonModsetStore(Path.Combine(root, "modsets.json")));
            var created = await manager.CreateAsync(new ModsetDraft(GameType.Ets2, "Start Test", null, Path.Combine(root, "home")));
            var startedAt = DateTimeOffset.UtcNow.AddSeconds(-1);

            var updated = await manager.MarkStartedAsync(created.Id, startedAt);

            Assert.Equal(created.Id, updated.Id);
            Assert.Equal(startedAt, updated.LastStartedAt);
            Assert.Equal(startedAt, Assert.Single(await manager.GetAllAsync()).LastStartedAt);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
