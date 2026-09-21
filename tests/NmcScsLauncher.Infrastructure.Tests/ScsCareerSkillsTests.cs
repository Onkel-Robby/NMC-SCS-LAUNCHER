using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsCareerSkillsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NmcScsLauncher-CareerSkillsTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReadsAndWritesAllCareerSkillsWithBackup()
    {
        var (save, editor) = await CreateAsync(
            """
            SiiNunit
            {
             adr: 5
             long_dist: 1
             heavy: 2
             fragile: 3
             urgent: 4
             mechanical: 5
            }
            """);

        var current = await editor.GetCareerSkillsAsync(save);

        Assert.Equal(5, current.AdrMask);
        Assert.Equal(1, current.LongDistance);
        Assert.Equal(2, current.HighValueCargo);
        Assert.Equal(3, current.FragileCargo);
        Assert.Equal(4, current.UrgentDelivery);
        Assert.Equal(5, current.EcoDriving);

        var result = await editor.SetCareerSkillsAsync(
            save,
            new ScsCareerSkills(
                AdrMask: 63,
                LongDistance: 6,
                HighValueCargo: 5,
                FragileCargo: 4,
                UrgentDelivery: 3,
                EcoDriving: 2));

        Assert.True(File.Exists(result.BackupPath));

        var text = await File.ReadAllTextAsync(save.GameSiiPath);
        Assert.Contains(" adr: 63", text);
        Assert.Contains(" long_dist: 6", text);
        Assert.Contains(" heavy: 5", text);
        Assert.Contains(" fragile: 4", text);
        Assert.Contains(" urgent: 3", text);
        Assert.Contains(" mechanical: 2", text);
    }

    [Fact]
    public async Task SetCareerSkillsRejectsOutOfRangeWithoutWriting()
    {
        var (save, editor) = await CreateAsync(
            """
            SiiNunit
            {
             adr: 0
             long_dist: 0
             heavy: 0
             fragile: 0
             urgent: 0
             mechanical: 0
            }
            """);
        var before = await File.ReadAllTextAsync(save.GameSiiPath);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            editor.SetCareerSkillsAsync(
                save,
                new ScsCareerSkills(0, 7, 0, 0, 0, 0)));

        Assert.Equal(before, await File.ReadAllTextAsync(save.GameSiiPath));
    }

    [Fact]
    public async Task ReadCareerSkillsFailsClosedWhenFieldIsAmbiguous()
    {
        var (save, editor) = await CreateAsync(
            """
            SiiNunit
            {
             adr: 0
             long_dist: 1
             long_dist: 2
             heavy: 0
             fragile: 0
             urgent: 0
             mechanical: 0
            }
            """);

        var exception = await Assert.ThrowsAsync<ScsSaveEditException>(() =>
            editor.GetCareerSkillsAsync(save));

        Assert.Contains("long_dist", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<(ScsSaveReference Save, ScsProfileEditService Editor)> CreateAsync(
        string content)
    {
        var profileDirectory = Path.Combine(_root, "profiles", "TEST");
        var saveDirectory = Path.Combine(profileDirectory, "save", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(saveDirectory);

        var gameSii = Path.Combine(saveDirectory, "game.sii");
        await File.WriteAllTextAsync(gameSii, content);

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
            Path.GetFileName(saveDirectory),
            "Save",
            ScsSaveKind.Manual,
            gameSii,
            DateTimeOffset.UtcNow);

        return (save, editor);
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
