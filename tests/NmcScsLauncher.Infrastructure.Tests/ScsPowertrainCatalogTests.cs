using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsPowertrainCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NmcScsLauncher-PowertrainCatalogTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CatalogContainsOnlyObservedPowertrainsForActiveTruckModel()
    {
        var (save, service) = await CreateAsync();

        var catalog = await service.GetActiveTruckPowertrainCatalogAsync(save);

        Assert.Equal("/def/vehicle/truck/volvo.fh16/", catalog.TruckModelRoot);

        Assert.Equal(2, catalog.Engines.Count);
        Assert.Contains(catalog.Engines, item =>
            item.DataPath == "/def/vehicle/truck/volvo.fh16/engine/d13.sii" &&
            item.IsCurrent);
        Assert.Contains(catalog.Engines, item =>
            item.DataPath == "/def/vehicle/truck/volvo.fh16/engine/d16.sii" &&
            !item.IsCurrent);
        Assert.DoesNotContain(catalog.Engines, item =>
            item.DataPath.Contains("scania.r", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(2, catalog.Transmissions.Count);
        Assert.Contains(catalog.Transmissions, item =>
            item.DataPath == "/def/vehicle/truck/volvo.fh16/transmission/i_shift.sii" &&
            item.IsCurrent);
        Assert.Contains(catalog.Transmissions, item =>
            item.DataPath == "/def/vehicle/truck/volvo.fh16/transmission/powershift.sii" &&
            !item.IsCurrent);
    }

    [Fact]
    public async Task CatalogDeduplicatesSameObservedDefinition()
    {
        var (save, service) = await CreateAsync(duplicateVolvoPowertrain: true);

        var catalog = await service.GetActiveTruckPowertrainCatalogAsync(save);

        Assert.Equal(
            2,
            catalog.Engines.Select(static item => item.DataPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.Equal(
            2,
            catalog.Transmissions.Select(static item => item.DataPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
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

    private async Task<(ScsSaveReference Save, ScsVehicleEditService Service)> CreateAsync(
        bool duplicateVolvoPowertrain = false)
    {
        var profileDirectory = Path.Combine(_root, "profiles", "TEST");
        var saveDirectory = Path.Combine(profileDirectory, "save", "1");
        Directory.CreateDirectory(saveDirectory);

        var gameSii = Path.Combine(saveDirectory, "game.sii");
        await File.WriteAllTextAsync(gameSii, CreateSave(duplicateVolvoPowertrain));

        var codec = new ScsPlainTextSaveCodec();
        var saveEdit = new ScsSaveEditService(
            codec,
            new NeverRunningGuard(),
            Path.Combine(_root, "backups"));
        var service = new ScsVehicleEditService(codec, saveEdit);

        var save = new ScsSaveReference(
            GameType.Ets2,
            profileDirectory,
            saveDirectory,
            "1",
            "Save 1",
            ScsSaveKind.Manual,
            gameSii,
            DateTimeOffset.UtcNow);

        return (save, service);
    }

    private static string CreateSave(bool duplicateVolvoPowertrain)
    {
        var thirdEngine = duplicateVolvoPowertrain
            ? "/def/vehicle/truck/volvo.fh16/engine/d16.sii"
            : "/def/vehicle/truck/scania.r/engine/v8.sii";
        var thirdTransmission = duplicateVolvoPowertrain
            ? "/def/vehicle/truck/volvo.fh16/transmission/powershift.sii"
            : "/def/vehicle/truck/scania.r/transmission/opticruise.sii";

        return $$"""
        SiiNunit
        {
        player : _nameless.player {
         assigned_vehicles: _nameless.player_vehicle.1
         trucks: 3
         trucks[0]: _nameless.truck.1
         trucks[1]: _nameless.truck.2
         trucks[2]: _nameless.truck.3
         trailers: 0
        }
        player_vehicles : _nameless.player_vehicle.1 {
         vehicle: _nameless.truck.1
         trailer: null
        }
        vehicle : _nameless.truck.1 {
         accessories: 2
         accessories[0]: _nameless.acc.1.engine
         accessories[1]: _nameless.acc.1.trans
        }
        vehicle : _nameless.truck.2 {
         accessories: 2
         accessories[0]: _nameless.acc.2.engine
         accessories[1]: _nameless.acc.2.trans
        }
        vehicle : _nameless.truck.3 {
         accessories: 2
         accessories[0]: _nameless.acc.3.engine
         accessories[1]: _nameless.acc.3.trans
        }
        vehicle_accessory : _nameless.acc.1.engine {
         data_path: "/def/vehicle/truck/volvo.fh16/engine/d13.sii"
        }
        vehicle_accessory : _nameless.acc.1.trans {
         data_path: "/def/vehicle/truck/volvo.fh16/transmission/i_shift.sii"
        }
        vehicle_accessory : _nameless.acc.2.engine {
         data_path: "/def/vehicle/truck/volvo.fh16/engine/d16.sii"
        }
        vehicle_accessory : _nameless.acc.2.trans {
         data_path: "/def/vehicle/truck/volvo.fh16/transmission/powershift.sii"
        }
        vehicle_accessory : _nameless.acc.3.engine {
         data_path: "{{thirdEngine}}"
        }
        vehicle_accessory : _nameless.acc.3.trans {
         data_path: "{{thirdTransmission}}"
        }
        }
        """;
    }

    private sealed class NeverRunningGuard : IScsGameProcessGuard
    {
        public bool IsAnyScsGameRunning() => false;
    }
}
