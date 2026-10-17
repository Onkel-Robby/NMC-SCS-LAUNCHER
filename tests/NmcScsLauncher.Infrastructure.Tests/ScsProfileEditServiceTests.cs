using System.Text;
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
    public async Task RenameProfileUpdatesProfileSiiAndHexDirectory()
    {
        var oldName = "ROBBY";
        var newName = "NMC Robby";
        var oldDirectoryName = Convert.ToHexString(Encoding.UTF8.GetBytes(oldName));
        var newDirectoryName = Convert.ToHexString(Encoding.UTF8.GetBytes(newName));
        var profileDirectory = Path.Combine(_root, "profiles", oldDirectoryName);
        Directory.CreateDirectory(profileDirectory);

        var profileSii = Path.Combine(profileDirectory, "profile.sii");
        await File.WriteAllTextAsync(
            profileSii,
            "SiiNunit\n{\nprofile_name: \"ROBBY\"\n}\n");

        var codec = new ScsPlainTextSaveCodec();
        var saveEdit = new ScsSaveEditService(
            codec,
            new NeverRunningGuard(),
            Path.Combine(_root, "backups"));
        var editor = new ScsProfileEditService(codec, saveEdit);

        var profile = new ScsProfileReference(
            GameType.Ets2,
            ScsProfileStorageKind.Local,
            _root,
            profileDirectory,
            oldDirectoryName,
            oldName,
            profileSii);

        var result = await editor.RenameProfileAsync(profile, newName);

        var renamedDirectory = Path.Combine(_root, "profiles", newDirectoryName);
        var renamedProfileSii = Path.Combine(renamedDirectory, "profile.sii");

        Assert.False(Directory.Exists(profileDirectory));
        Assert.True(Directory.Exists(renamedDirectory));
        Assert.True(File.Exists(renamedProfileSii));
        Assert.Equal(Path.GetFullPath(renamedProfileSii), result.FilePath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.Contains(
            "profile_name: \"NMC Robby\"",
            await File.ReadAllTextAsync(renamedProfileSii));
        Assert.Contains(
            "profile_name: \"ROBBY\"",
            await File.ReadAllTextAsync(result.BackupPath));

        var locator = new ScsProfileSaveLocator();
        var profiles = await locator.FindProfilesAsync(GameType.Ets2, _root);
        var renamed = Assert.Single(profiles);

        Assert.Equal(newName, renamed.DisplayName);
        Assert.Equal(Path.GetFullPath(renamedDirectory), renamed.ProfileDirectory);
    }

    [Fact]
    public async Task RenameProfileRejectsExistingTargetWithoutChangingSource()
    {
        var oldName = "ROBBY";
        var newName = "Existing";
        var oldDirectoryName = Convert.ToHexString(Encoding.UTF8.GetBytes(oldName));
        var newDirectoryName = Convert.ToHexString(Encoding.UTF8.GetBytes(newName));
        var profileDirectory = Path.Combine(_root, "profiles", oldDirectoryName);
        var targetDirectory = Path.Combine(_root, "profiles", newDirectoryName);
        Directory.CreateDirectory(profileDirectory);
        Directory.CreateDirectory(targetDirectory);

        var profileSii = Path.Combine(profileDirectory, "profile.sii");
        const string original = "SiiNunit\n{\nprofile_name: \"ROBBY\"\n}\n";
        await File.WriteAllTextAsync(profileSii, original);
        await File.WriteAllTextAsync(
            Path.Combine(targetDirectory, "profile.sii"),
            "SiiNunit\n{\nprofile_name: \"Existing\"\n}\n");

        var codec = new ScsPlainTextSaveCodec();
        var saveEdit = new ScsSaveEditService(
            codec,
            new NeverRunningGuard(),
            Path.Combine(_root, "backups"));
        var editor = new ScsProfileEditService(codec, saveEdit);

        var profile = new ScsProfileReference(
            GameType.Ets2,
            ScsProfileStorageKind.Local,
            _root,
            profileDirectory,
            oldDirectoryName,
            oldName,
            profileSii);

        var exception = await Assert.ThrowsAsync<ScsSaveEditException>(() =>
            editor.RenameProfileAsync(profile, newName));

        Assert.Contains("existiert bereits", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, await File.ReadAllTextAsync(profileSii));
        Assert.True(Directory.Exists(profileDirectory));
        Assert.True(Directory.Exists(targetDirectory));
    }

    [Fact]
    public async Task ReadsAndEditsReferencedBankAndEconomyValues()
    {
        var profileDirectory = Path.Combine(_root, "profiles", "524F424259");
        var saveDirectory = Path.Combine(profileDirectory, "save", "1");
        var backupRoot = Path.Combine(_root, "backups");
        Directory.CreateDirectory(saveDirectory);

        var gameSii = Path.Combine(saveDirectory, "game.sii");
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

        var moneyResult = await editor.SetMoneyAsync(save, 123456);
        var experienceResult = await editor.SetExperienceAsync(save, 98765);

        Assert.True(File.Exists(moneyResult.BackupPath));
        Assert.True(File.Exists(experienceResult.BackupPath));
        Assert.Equal(123456, await editor.GetMoneyAsync(save));
        Assert.Equal(98765, await editor.GetExperienceAsync(save));

        var gameText = (await File.ReadAllTextAsync(gameSii))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

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

    [Theory]
    [InlineData(ScsSaveKind.Manual, true)]
    [InlineData(ScsSaveKind.QuickSave, true)]
    [InlineData(ScsSaveKind.AutoSave, false)]
    [InlineData(ScsSaveKind.Unknown, false)]
    public async Task SaveKindControlsEditingWhileGameIsRunning(
        ScsSaveKind kind,
        bool shouldSucceed)
    {
        var profileDirectory = Path.Combine(_root, "profiles", "524F424259");
        var directoryName = kind switch
        {
            ScsSaveKind.QuickSave => "quicksave",
            ScsSaveKind.AutoSave => "autosave",
            ScsSaveKind.Unknown => "custom-save",
            _ => "1"
        };
        var saveDirectory = Path.Combine(profileDirectory, "save", directoryName);
        Directory.CreateDirectory(saveDirectory);

        var gameSii = Path.Combine(saveDirectory, "game.sii");
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
            }
            """);

        var lastWrite = new DateTimeOffset(File.GetLastWriteTimeUtc(gameSii));
        var codec = new ScsPlainTextSaveCodec();
        var saveEdit = new ScsSaveEditService(
            codec,
            new AlwaysRunningGuard(),
            Path.Combine(_root, "backups"));
        var editor = new ScsProfileEditService(codec, saveEdit);
        var save = new ScsSaveReference(
            GameType.Ets2,
            profileDirectory,
            saveDirectory,
            directoryName,
            directoryName,
            kind,
            gameSii,
            lastWrite);

        if (shouldSucceed)
        {
            await editor.SetMoneyAsync(save, 1234);
            Assert.Equal(1234, await editor.GetMoneyAsync(save));
        }
        else
        {
            await Assert.ThrowsAsync<ScsSaveEditException>(() =>
                editor.SetMoneyAsync(save, 1234));
            Assert.Equal(10, await editor.GetMoneyAsync(save));
        }
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

    private sealed class AlwaysRunningGuard : IScsGameProcessGuard
    {
        public bool IsAnyScsGameRunning() => true;
    }
}
