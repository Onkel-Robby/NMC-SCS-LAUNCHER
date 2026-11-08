using System.Security.Cryptography;
using System.Text;
using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed partial class ScsPowertrainResolutionScenarioTests
{
    [Theory]
    [InlineData(GameType.Ets2)]
    [InlineData(GameType.Ats)]
    public async Task LargeEconomyUnitSupportsRealDiscoveryVerifiedInstallAndByteExactRestore(GameType game)
    {
        using var fixture = new Fixture(game);
        var original = SaveWithLargeEconomyField().ReplaceLineEndings("\r\n");
        var path = fixture.Request.Save.GameSiiPath;
        // Preserve a real byte distinction (BOM and CRLF), not merely equivalent decoded text.
        await File.WriteAllTextAsync(path, original, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var originalBytes = await File.ReadAllBytesAsync(path);
        var originalSha = Convert.ToHexString(SHA256.HashData(originalBytes));
        var service = fixture.Service();
        var preview = await service.InspectAsync(fixture.Request);
        Assert.Equal(game, preview.Snapshot.Game);
        Assert.Equal(".truck", preview.Snapshot.TruckId);
        Assert.Equal(Model, preview.Snapshot.TruckModelRoot);
        Assert.Equal(originalSha, preview.Snapshot.SaveSha256);
        Assert.Equal(2, preview.Engines.Count);
        Assert.Equal(2, preview.Transmissions.Count);
        Assert.True(preview.AssessPair(NewEngine, NewTransmission).IsVerified);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));

        var backups = Path.Combine(fixture.Root, "managed-backups");
        var editService = new ScsSaveEditService(new ScsPlainTextSaveCodec(), new LargeUnitStoppedGuard(), backups);
        var installer = new ScsTruckPowertrainInstaller(service, editService, new LauncherFeatureFlags(true));
        var result = await installer.InstallAsync(new(fixture.Request, originalSha, preview.Snapshot.TruckId,
            NewEngine, NewTransmission));
        var expectedText = original.Replace(Model + "/engine/old.sii", NewEngine, StringComparison.Ordinal)
            .Replace(Model + "/transmission/old.sii", NewTransmission, StringComparison.Ordinal);
        var expectedBytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(expectedText)).ToArray();
        Assert.Equal(expectedBytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(result.BackupPath));
        Assert.Equal(originalSha, result.OriginalSha256, ignoreCase: true);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(expectedBytes)), result.UpdatedSha256, ignoreCase: true);

        var installed = await service.InspectAsync(fixture.Request);
        Assert.Equal(NewEngine, installed.Snapshot.CurrentEnginePath);
        Assert.Equal(NewTransmission, installed.Snapshot.CurrentTransmissionPath);
        Assert.True(installed.AssessPair(NewEngine, NewTransmission).IsVerified);
        await editService.RestoreBackupAsync(result.BackupPath, path);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        var restored = await service.InspectAsync(fixture.Request);
        Assert.Equal(originalSha, restored.Snapshot.SaveSha256);
        Assert.Equal(Model + "/engine/old.sii", restored.Snapshot.CurrentEnginePath);
        Assert.Equal(Model + "/transmission/old.sii", restored.Snapshot.CurrentTransmissionPath);
    }

    [Theory]
    [InlineData(GameType.Ets2)]
    [InlineData(GameType.Ats)]
    public async Task LargeIrrelevantEconomyArrayDoesNotHidePowertrainCandidates(GameType game)
    {
        using var fixture = new Fixture(game);
        const int count = 120_000;
        var fields = new StringBuilder(" player: .player\n stored_entries: ").Append(count).Append('\n');
        var value = new string('x', 64);
        for (var index = 0; index < count; index++)
            fields.Append(" stored_entries[").Append(index).Append("]: \"").Append(value).Append("\"\n");
        Assert.True(fields.Length > 8 * 1024 * 1024);
        var text = ScsTruckPowertrainSnapshotReaderTests.ValidSave().Replace(" player: .player\n", fields.ToString(), StringComparison.Ordinal);
        await File.WriteAllTextAsync(fixture.Request.Save.GameSiiPath, text);
        var originalSha = SHA256.HashData(await File.ReadAllBytesAsync(fixture.Request.Save.GameSiiPath));
        var preview = await fixture.Service().InspectAsync(fixture.Request);
        Assert.Equal(2, preview.Engines.Count);
        Assert.Equal(2, preview.Transmissions.Count);
        Assert.True(preview.AssessPair(NewEngine, NewTransmission).IsVerified);
        Assert.Equal(originalSha, SHA256.HashData(await File.ReadAllBytesAsync(fixture.Request.Save.GameSiiPath)));
    }

    [Theory]
    [InlineData(GameType.Ets2)]
    [InlineData(GameType.Ats)]
    public async Task MalformedTailOfLargeRequiredUnitCannotReachBackupOrWrite(GameType game)
    {
        using var fixture = new Fixture(game);
        var original = SaveWithLargeEconomyField(" invalid_tail: (1, )\n");
        var path = fixture.Request.Save.GameSiiPath;
        await File.WriteAllTextAsync(path, original);
        var originalBytes = await File.ReadAllBytesAsync(path);
        var originalSha = Convert.ToHexString(SHA256.HashData(originalBytes));
        var backupRoot = Path.Combine(fixture.Root, "managed-backups");
        var service = fixture.Service();
        var installer = new ScsTruckPowertrainInstaller(service,
            new ScsSaveEditService(new ScsPlainTextSaveCodec(), new LargeUnitStoppedGuard(), backupRoot), new LauncherFeatureFlags(true));
        await Assert.ThrowsAsync<ScsSaveEditException>(() => service.InspectAsync(fixture.Request));
        await Assert.ThrowsAsync<ScsSaveEditException>(() => installer.InstallAsync(new(fixture.Request, originalSha,
            ".truck", NewEngine, NewTransmission)));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        Assert.False(Directory.Exists(backupRoot));
    }

    private static string SaveWithLargeEconomyField(string tail = "") =>
        ScsTruckPowertrainSnapshotReaderTests.ValidSave().Replace(" player: .player\n",
            " player: .player\n stored_opaque_payload: \"" + new string('x', 9 * 1024 * 1024) + "\"\n" + tail,
            StringComparison.Ordinal);

    private sealed class LargeUnitStoppedGuard : IScsGameProcessGuard
    {
        public bool IsAnyScsGameRunning() => false;
    }
}
