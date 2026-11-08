using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class PowertrainFeatureGateTests
{
    private const string Disabled = "Die Motor-/Getriebefunktion ist vorübergehend deaktiviert.";

    [Theory]
    [InlineData("read")]
    [InlineData("catalog")]
    [InlineData("engine")]
    [InlineData("transmission")]
    public async Task DefaultVehicleServiceRejectsAllPowertrainEntriesBeforeAnyDependency(string operation)
    {
        IScsVehicleEditService service = new ScsVehicleEditService(new UnusedCodec(), new UnusedSaveWriter());
        var save = ContentRequest().Save;
        Func<Task> action = operation switch
        {
            "read" => () => service.GetActiveTruckPowertrainAsync(save),
            "catalog" => () => service.GetActiveTruckPowertrainCatalogAsync(save),
            "engine" => () => service.SetActiveTruckEngineAsync(save, "/def/vehicle/truck/test/engine/new.sii"),
            _ => () => service.SetActiveTruckTransmissionAsync(save, "/def/vehicle/truck/test/transmission/new.sii")
        };
        Assert.Equal(Disabled, (await Assert.ThrowsAsync<ScsSaveEditException>(action)).Message);
    }

    [Fact]
    public async Task DefaultInspectionRejectsDirectAndInterfaceEntryBeforeReadingSave()
    {
        var service = new ScsTruckPowertrainService(new(new UnusedCodec()), new UnusedContent());
        var request = ContentRequest();
        Assert.Equal(Disabled, (await Assert.ThrowsAsync<ScsSaveEditException>(() => service.InspectAsync(request))).Message);
        Assert.Equal(Disabled, (await Assert.ThrowsAsync<ScsSaveEditException>(() => ((IScsTruckPowertrainService)service).InspectAsync(request))).Message);
    }

    [Fact]
    public async Task DefaultDiscoveryRejectsDirectAndInterfaceEntryBeforeScanningInstallation()
    {
        var service = new ScsPowertrainDiscoveryService(new UnusedDetector(), new UnusedWorkshop());
        var request = new ScsPowertrainDiscoveryRequest(GameType.Ats, null, null, new("/def/vehicle/truck/test", [], []));
        Assert.Equal(Disabled, (await Assert.ThrowsAsync<ScsSaveEditException>(() => service.DiscoverAsync(request))).Message);
        Assert.Equal(Disabled, (await Assert.ThrowsAsync<ScsSaveEditException>(() => ((IScsPowertrainDiscoveryService)service).DiscoverAsync(request))).Message);
    }

    [Fact]
    public async Task DefaultInstallerRejectsDirectAndInterfaceEntryBeforeReadingOrWriting()
    {
        var inspection = new ScsTruckPowertrainService(new(new UnusedCodec()), new UnusedContent());
        var service = new ScsTruckPowertrainInstaller(inspection, new UnusedSaveWriter());
        var request = new ScsTruckPowertrainInstallRequest(ContentRequest(), new string('0', 64), ".truck",
            "/def/vehicle/truck/test/engine/new.sii", "/def/vehicle/truck/test/transmission/new.sii");
        Assert.Equal(Disabled, (await Assert.ThrowsAsync<ScsSaveEditException>(() => service.InstallAsync(request))).Message);
        Assert.Equal(Disabled, (await Assert.ThrowsAsync<ScsSaveEditException>(() => ((IScsTruckPowertrainInstaller)service).InstallAsync(request))).Message);
    }

    private static ScsEffectiveContentRequest ContentRequest()
    {
        // A unique path that is deliberately never created: disabled entry points must not perform any I/O.
        var home = Path.Combine(Path.GetTempPath(), "NmcDisabledPowertrain", Guid.NewGuid().ToString("N"));
        var profile = Path.Combine(home, "profiles", "TEST");
        var save = Path.Combine(profile, "save", "1");
        return new(new(GameType.Ats, ScsProfileStorageKind.Local, home, profile, "TEST", "Synthetic", Path.Combine(profile, "profile.sii")),
            new(GameType.Ats, profile, save, "1", "Synthetic", ScsSaveKind.Manual, Path.Combine(save, "game.sii"), DateTimeOffset.UnixEpoch), null);
    }

    private sealed class UnusedCodec : IScsSaveCodec
    {
        public Task<ScsSaveDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Codec must not be called.");
    }

    private sealed class UnusedSaveWriter : IScsSaveEditService
    {
        public Task<ScsSaveEditResult> ApplyAsync(ScsSaveEditRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Writer must not be called.");
        public Task RestoreBackupAsync(string backupPath, string targetFilePath, ScsSaveWriteMode writeMode = ScsSaveWriteMode.RequireGameStopped,
            DateTimeOffset? expectedLastWriteTimeUtc = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Restore must not be called.");
    }

    private sealed class UnusedContent : IScsEffectiveContentService
    {
        public Task<IScsEffectiveContentSession> OpenAsync(ScsEffectiveContentRequest request, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Sources must not be opened.");
    }

    private sealed class UnusedDetector : IGameInstallationDetector
    {
        public GameInstallation? Validate(GameType gameType, string installPath, GameInstallationSource source) => throw new InvalidOperationException("Installation must not be validated.");
        public Task<GameInstallation?> DetectAsync(GameType gameType, string? preferredInstallPath = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Installation must not be scanned.");
    }

    private sealed class UnusedWorkshop : IWorkshopContentLocator
    {
        public Task<WorkshopContentSnapshot> ScanAsync(GameType game, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Workshop must not be scanned.");
        public Task<WorkshopContentSnapshot> ScanAsync(GameType game, string? preferredInstallPath, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Workshop must not be scanned.");
    }
}
