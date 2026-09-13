using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ModsetRestoreServiceTests
{
    [Fact]
    public async Task RestoreRequiresConfirmationBeforeReplacingExistingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var manager = new ModsetManager(new JsonModsetStore(Path.Combine(root, "modsets.json")));

        var sourceHome = Path.Combine(root, "source");
        var source = await manager.CreateAsync(new ModsetDraft(GameType.Ets2, "Source", null, sourceHome));
        var sourceGameRoot = Path.Combine(sourceHome, GameDefinition.For(GameType.Ets2).HomeDirectoryName);
        Directory.CreateDirectory(sourceGameRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceGameRoot, "config.cfg"), "new-config");

        var archivePath = Path.Combine(root, "restore.zip");
        var backupService = new ModsetBackupService(manager);
        await backupService.CreateAsync(new ModsetBackupRequest(source.Id, archivePath, ModsetBackupContent.Configuration));

        var targetHome = Path.Combine(root, "target");
        var target = await manager.CreateAsync(new ModsetDraft(GameType.Ets2, "Target", null, targetHome));
        var targetGameRoot = Path.Combine(targetHome, GameDefinition.For(GameType.Ets2).HomeDirectoryName);
        Directory.CreateDirectory(targetGameRoot);
        var targetConfig = Path.Combine(targetGameRoot, "config.cfg");
        await File.WriteAllTextAsync(targetConfig, "old-config");

        var restoreService = new ModsetRestoreService(manager);
        var preview = await restoreService.InspectAsync(target.Id, archivePath);
        Assert.Equal(1, preview.FilesInArchive);
        Assert.Equal(1, preview.ExistingTargetFiles);

        await Assert.ThrowsAsync<ModsetBackupValidationException>(() => restoreService.RestoreAsync(
            new ModsetRestoreRequest(target.Id, archivePath, OverwriteExisting: false)));
        Assert.Equal("old-config", await File.ReadAllTextAsync(targetConfig));

        var result = await restoreService.RestoreAsync(
            new ModsetRestoreRequest(target.Id, archivePath, OverwriteExisting: true));
        Assert.Equal(1, result.FilesRestored);
        Assert.Equal(1, result.FilesOverwritten);
        Assert.Equal("new-config", await File.ReadAllTextAsync(targetConfig));
    }

    [Fact]
    public async Task RestoreRejectsBackupForDifferentGame()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var manager = new ModsetManager(new JsonModsetStore(Path.Combine(root, "modsets.json")));

        var sourceHome = Path.Combine(root, "source");
        var source = await manager.CreateAsync(new ModsetDraft(GameType.Ets2, "ETS2 Source", null, sourceHome));
        var sourceGameRoot = Path.Combine(sourceHome, GameDefinition.For(GameType.Ets2).HomeDirectoryName);
        Directory.CreateDirectory(sourceGameRoot);
        await File.WriteAllTextAsync(Path.Combine(sourceGameRoot, "config.cfg"), "config");
        var archivePath = Path.Combine(root, "ets2.zip");
        await new ModsetBackupService(manager).CreateAsync(
            new ModsetBackupRequest(source.Id, archivePath, ModsetBackupContent.Configuration));

        var atsTarget = await manager.CreateAsync(new ModsetDraft(GameType.Ats, "ATS Target", null, Path.Combine(root, "ats-target")));
        var restoreService = new ModsetRestoreService(manager);

        await Assert.ThrowsAsync<ModsetBackupValidationException>(() => restoreService.InspectAsync(atsTarget.Id, archivePath));
    }
}
