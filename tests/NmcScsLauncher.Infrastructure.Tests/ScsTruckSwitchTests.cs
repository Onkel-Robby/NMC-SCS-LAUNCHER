using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsTruckSwitchTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NmcScsLauncher-TruckSwitchTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SwitchTruckUpdatesPlayerVehiclesGarageDriversAndHq()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);

        var result = await editor.SwitchActiveTruckAsync(
            save,
            "_nameless.truck.2");

        Assert.True(File.Exists(result.BackupPath));

        var text = await File.ReadAllTextAsync(save.GameSiiPath);

        Assert.Equal(3, CountOccurrences(text, " vehicle: _nameless.truck.2"));
        Assert.DoesNotContain(" vehicle: _nameless.truck.1", text);

        Assert.Contains(" hq_city: hamburg", text);

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(
            "garage : garage.berlin {\n vehicles: 1\n vehicles[0]: _nameless.truck.1\n drivers: 1\n drivers[0]: _nameless.driver.1",
            normalized);
        Assert.Contains(
            "garage : garage.hamburg {\n vehicles: 1\n vehicles[0]: _nameless.truck.2\n drivers: 1\n drivers[0]: null",
            normalized);

        var state = await editor.InspectActiveVehiclesAsync(save);
        Assert.Equal("_nameless.truck.2", state.TruckId);
    }

    [Fact]
    public async Task SwitchTruckRejectsTruckOutsideOwnedInventory()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);
        var before = await File.ReadAllTextAsync(save.GameSiiPath);

        var exception = await Assert.ThrowsAsync<ScsSaveEditException>(() =>
            editor.SwitchActiveTruckAsync(
                save,
                "_nameless.truck.not_owned"));

        Assert.Contains("Besitz", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await File.ReadAllTextAsync(save.GameSiiPath));
    }

    [Fact]
    public async Task SwitchTruckFailsClosedWhenGarageDriverSlotIsMissing()
    {
        var source = CreateSave().Replace(
            " drivers: 1\n drivers[0]: _nameless.driver.1",
            " drivers: 0",
            StringComparison.Ordinal);

        var (save, codec, saveEdit) = await CreateAsync(source);
        var editor = new ScsVehicleEditService(codec, saveEdit);
        var before = await File.ReadAllTextAsync(save.GameSiiPath);

        var exception = await Assert.ThrowsAsync<ScsSaveEditException>(() =>
            editor.SwitchActiveTruckAsync(
                save,
                "_nameless.truck.2"));

        Assert.Contains("Fahrer-Slot", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await File.ReadAllTextAsync(save.GameSiiPath));
    }

    [Fact]
    public async Task SwitchTruckFailsClosedWhenTruckOccursInMultipleGarages()
    {
        var source = CreateSave() +
            """
            
            garage : garage.duplicate {
             vehicles: 1
             vehicles[0]: _nameless.truck.2
             drivers: 1
             drivers[0]: null
            }
            """;

        var (save, codec, saveEdit) = await CreateAsync(source);
        var editor = new ScsVehicleEditService(codec, saveEdit);
        var before = await File.ReadAllTextAsync(save.GameSiiPath);

        var exception = await Assert.ThrowsAsync<ScsSaveEditException>(() =>
            editor.SwitchActiveTruckAsync(
                save,
                "_nameless.truck.2"));

        Assert.Contains("mehreren Garagen", exception.Message, StringComparison.OrdinalIgnoreCase);
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
         hq_city: berlin
         trucks: 2
         trucks[0]: _nameless.truck.1
         trucks[1]: _nameless.truck.2
         trailers: 0
        }
        player_vehicles : _nameless.player_vehicle.1 {
         vehicle: _nameless.truck.1
         trailer: null
        }
        player_vehicles : _nameless.player_vehicle.2 {
         vehicle: _nameless.truck.1
         trailer: null
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
        garage : garage.berlin {
         vehicles: 1
         vehicles[0]: _nameless.truck.1
         drivers: 1
         drivers[0]: null
        }
        garage : garage.hamburg {
         vehicles: 1
         vehicles[0]: _nameless.truck.2
         drivers: 1
         drivers[0]: _nameless.driver.1
        }
        }
        """;

    private sealed class NeverRunningGuard : IScsGameProcessGuard
    {
        public bool IsAnyScsGameRunning() => false;
    }
}
