using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsSaveBackupServiceTests
{
    [Fact]
    public async Task BackupCopiesNestedSaveDataAndWritesManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        try
        {
            var profile = Path.Combine(root, "profiles", "profile");
            var saveDirectory = Path.Combine(profile, "save", "autosave");
            var nested = Path.Combine(saveDirectory, "nested");
            Directory.CreateDirectory(nested);
            var gameSii = Path.Combine(saveDirectory, "game.sii");
            await File.WriteAllTextAsync(gameSii, "SiiNunit\n{}");
            await File.WriteAllTextAsync(Path.Combine(nested, "payload.bin"), "payload");

            var save = new ScsSaveReference(
                GameType.Ats,
                profile,
                saveDirectory,
                "autosave",
                "Autosave",
                ScsSaveKind.AutoSave,
                gameSii,
                DateTimeOffset.UtcNow);

            var service = new ScsSaveBackupService(Path.Combine(root, "backups"));
            var result = await service.CreateAsync(save);

            Assert.Equal("SiiNunit\n{}", await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "game.sii")));
            Assert.Equal("payload", await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "nested", "payload.bin")));
            Assert.True(File.Exists(Path.Combine(result.BackupDirectory, "nmc-save-backup.json")));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
