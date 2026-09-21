using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsVehicleInventoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NmcScsLauncher-VehicleInventoryTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InventoryListsOwnedTrucksAndTrailersWithActiveFlags()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);

        var inventory = await editor.GetVehicleInventoryAsync(save);

        Assert.Equal("_nameless.truck.1", inventory.ActiveTruckId);
        Assert.Equal("_nameless.trailer.1", inventory.ActiveTrailerId);

        Assert.Equal(2, inventory.Trucks.Count);
        Assert.Contains(inventory.Trucks, item =>
            item.Id == "_nameless.truck.1" && item.Slot == 0 && item.IsActive);
        Assert.Contains(inventory.Trucks, item =>
            item.Id == "_nameless.truck.2" && item.Slot == 1 && !item.IsActive);

        Assert.Equal(2, inventory.Trailers.Count);
        Assert.Contains(inventory.Trailers, item =>
            item.Id == "_nameless.trailer.1" && item.Slot == 0 && item.IsActive);
        Assert.Contains(inventory.Trailers, item =>
            item.Id == "_nameless.trailer.3" && item.Slot == 1 && !item.IsActive);
    }

    [Fact]
    public async Task SwitchTrailerUpdatesOnlyPlayerVehicleUnitsForActiveTruck()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);

        var result = await editor.SwitchActiveTrailerAsync(
            save,
            "_nameless.trailer.3");

        Assert.True(File.Exists(result.BackupPath));

        var text = await File.ReadAllTextAsync(save.GameSiiPath);

        Assert.Contains(
            "player_vehicles : _nameless.player_vehicle.1",
            text);
        Assert.Contains(
            "player_vehicles : _nameless.player_vehicle.2",
            text);
        Assert.Equal(
            2,
            CountOccurrences(text, " trailer: _nameless.trailer.3"));

        var otherTruckBlock =
            "player_vehicles : _nameless.player_vehicle.3 {" +
            Environment.NewLine +
            " vehicle: _nameless.truck.2" +
            Environment.NewLine +
            " trailer: null";
        Assert.Contains(otherTruckBlock, text);

        var state = await editor.InspectActiveVehiclesAsync(save);
        Assert.Equal("_nameless.trailer.3", state.TrailerId);
        Assert.Equal(1, state.TrailerUnits);
    }

    [Fact]
    public async Task SwitchTrailerRejectsTrailerOutsideOwnedInventory()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);
        var before = await File.ReadAllTextAsync(save.GameSiiPath);

        var exception = await Assert.ThrowsAsync<ScsSaveEditException>(() =>
            editor.SwitchActiveTrailerAsync(
                save,
                "_nameless.trailer.not_owned"));

        Assert.Contains("Besitz", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await File.ReadAllTextAsync(save.GameSiiPath));
    }

    [Fact]
    public async Task SwitchTrailerFailsClosedOnConflictingPlayerVehicleReferences()
    {
        var source = CreateSave().Replace(
            "player_vehicles : _nameless.player_vehicle.2 {\n vehicle: _nameless.truck.1\n trailer: _nameless.trailer.1",
            "player_vehicles : _nameless.player_vehicle.2 {\n vehicle: _nameless.truck.1\n trailer: _nameless.trailer.3",
            StringComparison.Ordinal);

        var (save, codec, saveEdit) = await CreateAsync(source);
        var editor = new ScsVehicleEditService(codec, saveEdit);
        var before = await File.ReadAllTextAsync(save.GameSiiPath);

        var exception = await Assert.ThrowsAsync<ScsSaveEditException>(() =>
            editor.SwitchActiveTrailerAsync(
                save,
                "_nameless.trailer.3"));

        Assert.Contains(
            "unterschiedliche Trailer-Zuordnungen",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await File.ReadAllTextAsync(save.GameSiiPath));
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

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;

        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static string CreateSave() =>
        """
        SiiNunit
        {
        player : _nameless.player {
         assigned_vehicles: _nameless.player_vehicle.1
         trucks: 2
         trucks[0]: _nameless.truck.1
         trucks[1]: _nameless.truck.2
         trailers: 2
         trailers[0]: _nameless.trailer.1
         trailers[1]: _nameless.trailer.3
        }
        player_vehicles : _nameless.player_vehicle.1 {
         vehicle: _nameless.truck.1
         trailer: _nameless.trailer.1
        }
        player_vehicles : _nameless.player_vehicle.2 {
         vehicle: _nameless.truck.1
         trailer: _nameless.trailer.1
        }
        player_vehicles : _nameless.player_vehicle.3 {
         vehicle: _nameless.truck.2
         trailer: null
        }
        vehicle : _nameless.truck.1 {
         fuel_relative: 0.25
         engine_wear: 0
         transmission_wear: 0
         cabin_wear: 0
         chassis_wear: 0
         wheels_wear[0]: 0
        }
        vehicle : _nameless.truck.2 {
         fuel_relative: 0.5
         engine_wear: 0
         transmission_wear: 0
         cabin_wear: 0
         chassis_wear: 0
         wheels_wear[0]: 0
        }
        trailer : _nameless.trailer.1 {
         trailer_body_wear: 0
         chassis_wear: 0
         wheels_wear[0]: 0
         slave_trailer: _nameless.trailer.2
        }
        trailer : _nameless.trailer.2 {
         trailer_body_wear: 0
         chassis_wear: 0
         wheels_wear[0]: 0
         slave_trailer: null
        }
        trailer : _nameless.trailer.3 {
         trailer_body_wear: 0
         chassis_wear: 0
         wheels_wear[0]: 0
         slave_trailer: null
        }
        }
        """;

    private sealed class NeverRunningGuard : IScsGameProcessGuard
    {
        public bool IsAnyScsGameRunning() => false;
    }
}
