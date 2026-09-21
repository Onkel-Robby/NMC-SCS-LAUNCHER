using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsVehicleEditServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NmcScsLauncher-VehicleEditTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InspectRepairTruckSetFuelAndRepairTrailerChain()
    {
        var profileDirectory = Path.Combine(_root, "profiles", "TEST");
        var saveDirectory = Path.Combine(profileDirectory, "save", "1");
        var backupRoot = Path.Combine(_root, "backups");
        Directory.CreateDirectory(saveDirectory);

        var gameSii = Path.Combine(saveDirectory, "game.sii");
        await File.WriteAllTextAsync(gameSii, CreateSave());

        var codec = new ScsPlainTextSaveCodec();
        var saveEdit = new ScsSaveEditService(codec, new NeverRunningGuard(), backupRoot);
        var editor = new ScsVehicleEditService(codec, saveEdit);
        var save = CreateReference(profileDirectory, saveDirectory, gameSii);

        var state = await editor.InspectActiveVehiclesAsync(save);

        Assert.Equal("_nameless.truck.1", state.TruckId);
        Assert.Equal("_nameless.trailer.1", state.TrailerId);
        Assert.Equal(2, state.TrailerUnits);
        Assert.Equal(0.25m, state.FuelRelative);

        var truckRepair = await editor.RepairActiveTruckAsync(save);
        var fuel = await editor.SetActiveTruckFuelAsync(save, 0.8m);
        var trailerRepair = await editor.RepairActiveTrailerAsync(save);

        Assert.True(File.Exists(truckRepair.BackupPath));
        Assert.True(File.Exists(fuel.BackupPath));
        Assert.True(File.Exists(trailerRepair.BackupPath));

        var text = await File.ReadAllTextAsync(gameSii);

        Assert.Contains(" fuel_relative: 0.8", text);
        Assert.Contains(" engine_wear: 0", text);
        Assert.Contains(" transmission_wear: 0", text);
        Assert.Contains(" cabin_wear_unfixable: 0", text);
        Assert.Contains(" wheels_wear[0]: 0", text);
        Assert.Contains(" wheels_wear_unfixable[0]: 0", text);

        Assert.Contains(" trailer_body_wear: 0", text);
        Assert.Contains(" trailer_body_wear_unfixable: 0", text);
        Assert.Contains(" slave_trailer: _nameless.trailer.2", text);

        Assert.DoesNotContain(" engine_wear: 0.4", text);
        Assert.DoesNotContain(" trailer_body_wear: 0.6", text);
        Assert.DoesNotContain(" trailer_body_wear: 0.2", text);
    }

    [Fact]
    public async Task RepairTruck_DoesNotTouchOtherTruck()
    {
        var profileDirectory = Path.Combine(_root, "profiles", "TEST");
        var saveDirectory = Path.Combine(profileDirectory, "save", "1");
        Directory.CreateDirectory(saveDirectory);

        var gameSii = Path.Combine(saveDirectory, "game.sii");
        var source = CreateSave() +
            "\nvehicle : _nameless.truck.2 {\n engine_wear: 0.9\n fuel_relative: 0.1\n}\n";
        await File.WriteAllTextAsync(gameSii, source);

        var codec = new ScsPlainTextSaveCodec();
        var saveEdit = new ScsSaveEditService(
            codec,
            new NeverRunningGuard(),
            Path.Combine(_root, "backups"));
        var editor = new ScsVehicleEditService(codec, saveEdit);

        await editor.RepairActiveTruckAsync(
            CreateReference(profileDirectory, saveDirectory, gameSii));

        var text = (await File.ReadAllTextAsync(gameSii))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("vehicle : _nameless.truck.2 {\n engine_wear: 0.9", text);
    }

    [Fact]
    public async Task FuelRejectsOutOfRangeValues()
    {
        var profileDirectory = Path.Combine(_root, "profiles", "TEST");
        var saveDirectory = Path.Combine(profileDirectory, "save", "1");
        Directory.CreateDirectory(saveDirectory);

        var gameSii = Path.Combine(saveDirectory, "game.sii");
        await File.WriteAllTextAsync(gameSii, CreateSave());

        var codec = new ScsPlainTextSaveCodec();
        var saveEdit = new ScsSaveEditService(
            codec,
            new NeverRunningGuard(),
            Path.Combine(_root, "backups"));
        var editor = new ScsVehicleEditService(codec, saveEdit);
        var save = CreateReference(profileDirectory, saveDirectory, gameSii);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            editor.SetActiveTruckFuelAsync(save, 1.1m));
    }

    [Fact]
    public async Task TrailerRepairRejectsCycle()
    {
        var profileDirectory = Path.Combine(_root, "profiles", "TEST");
        var saveDirectory = Path.Combine(profileDirectory, "save", "1");
        Directory.CreateDirectory(saveDirectory);

        var gameSii = Path.Combine(saveDirectory, "game.sii");
        var source = CreateSave().Replace(
            " slave_trailer: null",
            " slave_trailer: _nameless.trailer.1",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(gameSii, source);

        var codec = new ScsPlainTextSaveCodec();
        var saveEdit = new ScsSaveEditService(
            codec,
            new NeverRunningGuard(),
            Path.Combine(_root, "backups"));
        var editor = new ScsVehicleEditService(codec, saveEdit);

        var exception = await Assert.ThrowsAsync<ScsSaveEditException>(() =>
            editor.RepairActiveTrailerAsync(
                CreateReference(profileDirectory, saveDirectory, gameSii)));

        Assert.Contains("Zyklus", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    private static ScsSaveReference CreateReference(
        string profileDirectory,
        string saveDirectory,
        string gameSii) =>
        new(
            GameType.Ets2,
            profileDirectory,
            saveDirectory,
            "1",
            "Save 1",
            ScsSaveKind.Manual,
            gameSii,
            DateTimeOffset.UtcNow);

    private static string CreateSave() =>
        """
        SiiNunit
        {
        player : _nameless.player {
         assigned_vehicles: _nameless.player_vehicle.1
        }
        player_vehicles : _nameless.player_vehicle.1 {
         vehicle: _nameless.truck.1
         trailer: _nameless.trailer.1
        }
        vehicle : _nameless.truck.1 {
         fuel_relative: 0.25
         engine_wear: 0.4
         transmission_wear: 0.3
         cabin_wear: 0.2
         engine_wear_unfixable: 0.1
         transmission_wear_unfixable: 0.1
         cabin_wear_unfixable: 0.1
         chassis_wear: 0.5
         chassis_wear_unfixable: 0.1
         wheels_wear[0]: 0.4
         wheels_wear_unfixable[0]: 0.1
        }
        trailer : _nameless.trailer.1 {
         trailer_body_wear: 0.6
         trailer_body_wear_unfixable: 0.1
         chassis_wear: 0.3
         chassis_wear_unfixable: 0.1
         wheels_wear[0]: 0.5
         wheels_wear_unfixable[0]: 0.1
         slave_trailer: _nameless.trailer.2
        }
        trailer : _nameless.trailer.2 {
         trailer_body_wear: 0.2
         trailer_body_wear_unfixable: 0.1
         chassis_wear: 0.2
         chassis_wear_unfixable: 0.1
         wheels_wear[0]: 0.2
         wheels_wear_unfixable[0]: 0.1
         slave_trailer: null
        }
        }
        """;

    private sealed class NeverRunningGuard : IScsGameProcessGuard
    {
        public bool IsAnyScsGameRunning() => false;
    }
}
