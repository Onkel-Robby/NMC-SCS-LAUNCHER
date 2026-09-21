using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsProfileEditServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NmcScsLauncher-ProfileEditTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RenamesProfileReadsAndEditsReferencedBankAndEconomyValues()
    {
        var profileDirectory = Path.Combine(_root, "profiles", "524F424259");
        var saveDirectory = Path.Combine(profileDirectory, "save", "1");
        var backupRoot = Path.Combine(_root, "backups");
        Directory.CreateDirectory(saveDirectory);

        var profileSii = Path.Combine(profileDirectory, "profile.sii");
        var gameSii = Path.Combine(saveDirectory, "game.sii");

        await File.WriteAllTextAsync(
            profileSii,
            "SiiNunit\n{\nprofile_name: \"Old Name\"\n}\n");
        await File.WriteAllTextAsync(
            gameSii,
            """
            SiiNunit
            {
            economy : _nameless.economy {
             bank: _nameless.bank
             experience_points: 20
            }
            bank : _nameless.bank {
             money_account: 10
            }
            driver_ai : driver.1 {
             experience_points: 999
            }
            }
            """);

        var codec = new ScsPlainTextSaveCodec();
        var saveEdit = new ScsSaveEditService(codec, new NeverRunningGuard(), backupRoot);
        var editor = new ScsProfileEditService(codec, saveEdit);

        var profile = new ScsProfileReference(
            GameType.Ets2,
            ScsProfileStorageKind.Local,
            _root,
            profileDirectory,
            "524F424259",
            "ROBBY",
            profileSii);

        var save = new ScsSaveReference(
            GameType.Ets2,
            profileDirectory,
            saveDirectory,
            "1",
            "Save 1",
            ScsSaveKind.Manual,
            gameSii,
            DateTimeOffset.UtcNow);

        Assert.Equal(10, await editor.GetMoneyAsync(save));
        Assert.Equal(20, await editor.GetExperienceAsync(save));

        var renameResult = await editor.RenameProfileAsync(profile, "NMC Robby");
        var moneyResult = await editor.SetMoneyAsync(save, 123456);
        var experienceResult = await editor.SetExperienceAsync(save, 98765);

        Assert.True(File.Exists(renameResult.BackupPath));
        Assert.True(File.Exists(moneyResult.BackupPath));
        Assert.True(File.Exists(experienceResult.BackupPath));
        Assert.Equal(123456, await editor.GetMoneyAsync(save));
        Assert.Equal(98765, await editor.GetExperienceAsync(save));

        var profileText = await File.ReadAllTextAsync(profileSii);
        var gameText = (await File.ReadAllTextAsync(gameSii))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("profile_name: \"NMC Robby\"", profileText);
        Assert.Contains(
            "economy : _nameless.economy {\n bank: _nameless.bank\n experience_points: 98765",
            gameText);
        Assert.Contains(
            "bank : _nameless.bank {\n money_account: 123456",
            gameText);
        Assert.Contains(
            "driver_ai : driver.1 {\n experience_points: 999",
            gameText);
    }

    [Fact]
    public async Task SetMoney_FailsClosedWhenReferencedBankFieldIsAmbiguous()
    {
        var profileDirectory = Path.Combine(_root, "profiles", "TEST");
        var saveDirectory = Path.Combine(profileDirectory, "save", "1");
        Directory.CreateDirectory(saveDirectory);

        var gameSii = Path.Combine(saveDirectory, "game.sii");
        const string original =
            "SiiNunit\n{\neconomy : .economy {\n bank: .bank\n}\nbank : .bank {\n money_account: 10\n money_account: 20\n}\n}\n";
        await File.WriteAllTextAsync(gameSii, original);

        var codec = new ScsPlainTextSaveCodec();
        var saveEdit = new ScsSaveEditService(
            codec,
            new NeverRunningGuard(),
            Path.Combine(_root, "backups"));
        var editor = new ScsProfileEditService(codec, saveEdit);

        var save = new ScsSaveReference(
            GameType.Ets2,
            profileDirectory,
            saveDirectory,
            "1",
            "Save 1",
            ScsSaveKind.Manual,
            gameSii,
            DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<ScsSaveEditException>(() =>
            editor.SetMoneyAsync(save, 100));

        Assert.Equal(original, await File.ReadAllTextAsync(gameSii));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class NeverRunningGuard : IScsGameProcessGuard
    {
        public bool IsAnyScsGameRunning() => false;
    }
}
