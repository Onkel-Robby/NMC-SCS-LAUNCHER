using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsSaveEditServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NmcScsLauncher-SaveEditTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ApplyAndRestore_CreatesBackupAndRoundTripsTextSave()
    {
        Directory.CreateDirectory(_root);
        var saveDir = Path.Combine(_root, "save", "1");
        var backupRoot = Path.Combine(_root, "backups");
        Directory.CreateDirectory(saveDir);

        var target = Path.Combine(saveDir, "profile.sii");
        const string original = "SiiNunit\n{\n profile_name: \"Original\"\n}\n";
        await File.WriteAllTextAsync(target, original);

        var codec = new ScsPlainTextSaveCodec();
        var service = new ScsSaveEditService(codec, new NeverRunningGuard(), backupRoot);
        var document = ScsSiiTextDocument.Parse((await codec.ReadAsync(target)).Content);
        var updated = document.SetScalar("profile_name", "\"NMC\"").ToText();

        var result = await service.ApplyAsync(new ScsSaveEditRequest(
            target,
            "Rename profile",
            updated));

        Assert.True(File.Exists(result.BackupPath));
        Assert.Contains(" profile_name: \"NMC\"", await File.ReadAllTextAsync(target));
        Assert.NotEqual(result.OriginalSha256, result.UpdatedSha256);

        await service.RestoreBackupAsync(result.BackupPath, target);

        Assert.Equal(original, await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task Apply_BlocksWhenGameIsRunning()
    {
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "game.sii");
        await File.WriteAllTextAsync(target, "SiiNunit\n{\n money_account: 1\n}\n");

        var service = new ScsSaveEditService(
            new ScsPlainTextSaveCodec(),
            new AlwaysRunningGuard(),
            Path.Combine(_root, "backups"));

        await Assert.ThrowsAsync<ScsSaveEditException>(() =>
            service.ApplyAsync(new ScsSaveEditRequest(
                target,
                "Money test",
                "SiiNunit\n{\n money_account: 2\n}\n")));
    }

    [Fact]
    public async Task Codec_RejectsBinarySave()
    {
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "game.sii");
        await File.WriteAllBytesAsync(target, [0x00, 0x01, 0x02, 0x03]);

        var codec = new ScsPlainTextSaveCodec();

        await Assert.ThrowsAsync<ScsSaveEditException>(() => codec.ReadAsync(target));
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
