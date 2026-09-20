using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsSaveEditServiceTests
{
    [Fact]
    public async Task SetMoneyCreatesFullBackupBeforeReplacingGameSii()
    {
        var root = CreateRoot();
        try
        {
            var original = """
                SiiNunit
                {
                 bank : _nameless.1 {
                  money_account: 100
                 }
                 player : _nameless.2 {
                  experience_points: 25
                 }
                }
                """;
            var save = await CreateSaveAsync(root, original);
            await File.WriteAllTextAsync(Path.Combine(save.SaveDirectory, "info.sii"), "save-info");

            var backupRoot = Path.Combine(root, "backups");
            var service = CreateService(backupRoot);

            var result = await service.SetMoneyAsync(save, 250);

            Assert.True(result.Succeeded, $"{result.Status}: {result.Message}");
            Assert.NotNull(result.BackupDirectory);
            Assert.Contains("money_account: 250", await File.ReadAllTextAsync(save.GameSiiPath));
            Assert.Contains("experience_points: 25", await File.ReadAllTextAsync(save.GameSiiPath));

            var backupGame = Path.Combine(result.BackupDirectory!, "game.sii");
            Assert.Equal(original, await File.ReadAllTextAsync(backupGame));
            Assert.Equal("save-info", await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory!, "info.sii")));
            Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "nmc-save-backup.json")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task SetExperienceFailsClosedWhenGameIsRunning()
    {
        var root = CreateRoot();
        try
        {
            var original = "SiiNunit\n{\n experience_points: 25\n}\n";
            var save = await CreateSaveAsync(root, original);
            var service = CreateService(Path.Combine(root, "backups"), running: true);

            var result = await service.SetExperienceAsync(save, 1000);

            Assert.Equal(ScsSaveEditStatus.GameRunning, result.Status);
            Assert.Equal(original, await File.ReadAllTextAsync(save.GameSiiPath));
            Assert.False(Directory.Exists(Path.Combine(root, "backups")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task EncryptedOrBinarySaveFailsClosedBeforeBackup()
    {
        var root = CreateRoot();
        try
        {
            var save = await CreateSaveAsync(root, "ScsC-not-plaintext");
            var backupRoot = Path.Combine(root, "backups");
            var service = CreateService(backupRoot);

            var result = await service.SetMoneyAsync(save, 1000);

            Assert.Equal(ScsSaveEditStatus.UnsupportedFormat, result.Status);
            Assert.Equal("ScsC-not-plaintext", await File.ReadAllTextAsync(save.GameSiiPath));
            Assert.False(Directory.Exists(backupRoot));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DuplicateScalarFieldIsRejectedWithoutChangingSave()
    {
        var root = CreateRoot();
        try
        {
            var original = "SiiNunit\n{\n money_account: 1\n money_account: 2\n}\n";
            var save = await CreateSaveAsync(root, original);
            var service = CreateService(Path.Combine(root, "backups"));

            var result = await service.SetMoneyAsync(save, 500);

            Assert.Equal(ScsSaveEditStatus.AmbiguousProperty, result.Status);
            Assert.Equal(original, await File.ReadAllTextAsync(save.GameSiiPath));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task GameSiiOutsideSelectedSaveDirectoryIsRejected()
    {
        var root = CreateRoot();
        try
        {
            var save = await CreateSaveAsync(root, "SiiNunit\n{\n money_account: 1\n}\n");
            var outside = Path.Combine(root, "outside", "game.sii");
            Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
            await File.WriteAllTextAsync(outside, "SiiNunit\n{\n money_account: 1\n}\n");

            var invalid = save with { GameSiiPath = outside };
            var service = CreateService(Path.Combine(root, "backups"));

            var result = await service.SetMoneyAsync(invalid, 500);

            Assert.Equal(ScsSaveEditStatus.InvalidSelection, result.Status);
            Assert.Contains("außerhalb", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static ScsSaveEditService CreateService(string backupRoot, bool running = false) =>
        new(
            new ScsSaveBackupService(backupRoot),
            new PlainTextScsSaveCodec(),
            new FakeProcessGuard(running));

    private static async Task<ScsSaveReference> CreateSaveAsync(string root, string gameSii)
    {
        var profile = Path.Combine(root, "profiles", "profile");
        var saveDirectory = Path.Combine(profile, "save", "1");
        Directory.CreateDirectory(saveDirectory);
        var gameSiiPath = Path.Combine(saveDirectory, "game.sii");
        await File.WriteAllTextAsync(gameSiiPath, gameSii);

        return new ScsSaveReference(
            GameType.Ets2,
            profile,
            saveDirectory,
            "1",
            "Save 1",
            ScsSaveKind.Manual,
            gameSiiPath,
            DateTimeOffset.UtcNow);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private sealed class FakeProcessGuard(bool running) : IScsGameProcessGuard
    {
        public bool IsGameRunning(GameType gameType) => running;
    }
}
