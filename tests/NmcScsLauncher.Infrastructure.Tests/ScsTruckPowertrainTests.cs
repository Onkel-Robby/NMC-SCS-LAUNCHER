using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsTruckPowertrainTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NmcScsLauncher-PowertrainTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InspectPowertrainResolvesExactlyOneEngineAndTransmission()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);

        var powertrain = await editor.GetActiveTruckPowertrainAsync(save);

        Assert.Equal(
            "/def/vehicle/truck/volvo.fh16/engine/d13.sii",
            powertrain.EngineDataPath);
        Assert.Equal(
            "/def/vehicle/truck/volvo.fh16/transmission/i_shift.sii",
            powertrain.TransmissionDataPath);
    }

    [Fact]
    public async Task SetEngineChangesOnlyEngineAccessory()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);

        var result = await editor.SetActiveTruckEngineAsync(
            save,
            "/def/vehicle/truck/volvo.fh16/engine/d16.sii");

        Assert.True(File.Exists(result.BackupPath));

        var text = await File.ReadAllTextAsync(save.GameSiiPath);
        Assert.Contains(
            "data_path: \"/def/vehicle/truck/volvo.fh16/engine/d16.sii\"",
            text);
        Assert.Contains(
            "data_path: \"/def/vehicle/truck/volvo.fh16/transmission/i_shift.sii\"",
            text);
        Assert.Contains(
            "data_path: \"/def/vehicle/truck/volvo.fh16/cabin/sleeper.sii\"",
            text);
        Assert.DoesNotContain(
            "data_path: \"/def/vehicle/truck/volvo.fh16/engine/d13.sii\"",
            text);
    }

    [Fact]
    public async Task SetTransmissionChangesOnlyTransmissionAccessory()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);

        var result = await editor.SetActiveTruckTransmissionAsync(
            save,
            "/def/vehicle/truck/volvo.fh16/transmission/powershift.sii");

        Assert.True(File.Exists(result.BackupPath));

        var text = await File.ReadAllTextAsync(save.GameSiiPath);
        Assert.Contains(
            "data_path: \"/def/vehicle/truck/volvo.fh16/transmission/powershift.sii\"",
            text);
        Assert.Contains(
            "data_path: \"/def/vehicle/truck/volvo.fh16/engine/d13.sii\"",
            text);
    }

    [Fact]
    public async Task SetEngineRejectsTransmissionPathWithoutWriting()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);
        var before = await File.ReadAllTextAsync(save.GameSiiPath);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            editor.SetActiveTruckEngineAsync(
                save,
                "/def/vehicle/truck/volvo.fh16/transmission/i_shift.sii"));

        Assert.Equal(before, await File.ReadAllTextAsync(save.GameSiiPath));
    }

    [Fact]
    public async Task InspectPowertrainFailsClosedOnMultipleEngineAccessories()
    {
        var source = CreateSave()
            .Replace(
                " accessories: 3",
                " accessories: 4",
                StringComparison.Ordinal)
            .Replace(
                " accessories[2]: _nameless.accessory.cabin",
                " accessories[2]: _nameless.accessory.cabin" +
                Environment.NewLine +
                " accessories[3]: _nameless.accessory.engine.2",
                StringComparison.Ordinal);

        var rootEnd = source.LastIndexOf('}');
        Assert.True(rootEnd >= 0);

        source = source.Insert(
            rootEnd,
            """
            vehicle_accessory : _nameless.accessory.engine.2 {
             data_path: "/def/vehicle/truck/volvo.fh16/engine/d16.sii"
            }
            
            """);

        var (save, codec, saveEdit) = await CreateAsync(source);
        var editor = new ScsVehicleEditService(codec, saveEdit);

        var exception = await Assert.ThrowsAsync<ScsSaveEditException>(() =>
            editor.GetActiveTruckPowertrainAsync(save));

        Assert.Contains(
            "Engine-Accessories",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetTransmissionRejectsAmbiguousDefinitionPath()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            editor.SetActiveTruckTransmissionAsync(
                save,
                "/def/vehicle/truck/volvo.fh16/engine/transmission/test.sii"));
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

    private async Task<(ScsSaveReference Save, ScsPlainTextSaveCodec Codec, ScsSaveEditService SaveEdit)>
        CreateAsync(string content)
    {
        var profileDirectory = Path.Combine(_root, "profiles", "TEST");
        var saveDirectory = Path.Combine(profileDirectory, "save", "1");
        Directory.CreateDirectory(saveDirectory);

        var gameSii = Path.Combine(saveDirectory, "game.sii");
        await File.WriteAllTextAsync(gameSii, content);

        var codec = new ScsPlainTextSaveCodec();
        var saveEdit = new ScsSaveEditService(
            codec,
            new NeverRunningGuard(),
            Path.Combine(_root, "backups"));

        var save = new ScsSaveReference(
            GameType.Ets2,
            profileDirectory,
            saveDirectory,
            "1",
            "Save 1",
            ScsSaveKind.Manual,
            gameSii,
            DateTimeOffset.UtcNow);

        return (save, codec, saveEdit);
    }

    private static string CreateSave() =>
        """
        SiiNunit
        {
        player : _nameless.player {
         assigned_vehicles: _nameless.player_vehicle.1
         trucks: 1
         trucks[0]: _nameless.truck.1
         trailers: 0
        }
        player_vehicles : _nameless.player_vehicle.1 {
         vehicle: _nameless.truck.1
         trailer: null
        }
        vehicle : _nameless.truck.1 {
         accessories: 3
         accessories[0]: _nameless.accessory.engine
         accessories[1]: _nameless.accessory.transmission
         accessories[2]: _nameless.accessory.cabin
        }
        vehicle_accessory : _nameless.accessory.engine {
         data_path: "/def/vehicle/truck/volvo.fh16/engine/d13.sii"
        }
        vehicle_accessory : _nameless.accessory.transmission {
         data_path: "/def/vehicle/truck/volvo.fh16/transmission/i_shift.sii"
        }
        vehicle_accessory : _nameless.accessory.cabin {
         data_path: "/def/vehicle/truck/volvo.fh16/cabin/sleeper.sii"
        }
        }
        """;

    private sealed class NeverRunningGuard : IScsGameProcessGuard
    {
        public bool IsAnyScsGameRunning() => false;
    }
}
