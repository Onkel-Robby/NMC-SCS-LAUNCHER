using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsVehicleValuesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NmcScsLauncher-VehicleValuesTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SetMileageUpdatesTruckAndMatchingProfitLog()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);

        var result = await editor.SetActiveTruckMileageAsync(save, 123456.75m);

        Assert.True(File.Exists(result.BackupPath));

        var text = await File.ReadAllTextAsync(save.GameSiiPath);
        Assert.Contains(" odometer: 123456.75", text);
        Assert.Contains(" integrity_odometer: 123456.75", text);
        Assert.Contains(" trip_distance_km: 123456.75", text);
        Assert.Contains(" acc_distance_on_job: 123456.75", text);
        Assert.Contains(" acc_distance_free: 0", text);

        Assert.Contains(" odometer: 222", text);
        Assert.Contains(" acc_distance_on_job: 333", text);
    }

    [Fact]
    public async Task SetMileageFailsClosedWhenMatchingProfitLogIsMissing()
    {
        var source = CreateSave()
            .Replace(
                " truck_profit_logs[0]: _nameless.profit.1",
                " truck_profit_logs[5]: _nameless.profit.1",
                StringComparison.Ordinal);

        var (save, codec, saveEdit) = await CreateAsync(source);
        var editor = new ScsVehicleEditService(codec, saveEdit);
        var before = await File.ReadAllTextAsync(save.GameSiiPath);

        var exception = await Assert.ThrowsAsync<ScsSaveEditException>(() =>
            editor.SetActiveTruckMileageAsync(save, 1000m));

        Assert.Contains("truck_profit_logs", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await File.ReadAllTextAsync(save.GameSiiPath));
    }

    [Fact]
    public async Task SetTrailerCargoMassUpdatesActiveTrailerChainOnly()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);

        var result = await editor.SetActiveTrailerCargoMassAsync(save, 25000.5m);

        Assert.True(File.Exists(result.BackupPath));

        var text = await File.ReadAllTextAsync(save.GameSiiPath);
        Assert.Equal(2, CountOccurrences(text, " cargo_mass: 25000.5"));
        Assert.Contains(" cargo_mass: 999", text);
    }

    [Fact]
    public async Task SetTrailerCargoMassRejectsNegativeValueWithoutWriting()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);
        var before = await File.ReadAllTextAsync(save.GameSiiPath);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            editor.SetActiveTrailerCargoMassAsync(save, -1m));

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
         truck_profit_logs: 2
         truck_profit_logs[0]: _nameless.profit.1
         truck_profit_logs[1]: _nameless.profit.2
         trailers: 2
         trailers[0]: _nameless.trailer.1
         trailers[1]: _nameless.trailer.3
        }
        player_vehicles : _nameless.player_vehicle.1 {
         vehicle: _nameless.truck.1
         trailer: _nameless.trailer.1
        }
        vehicle : _nameless.truck.1 {
         odometer: 100
         integrity_odometer: 100
         trip_distance_km: 100
         fuel_relative: 0.25
        }
        vehicle : _nameless.truck.2 {
         odometer: 222
         integrity_odometer: 222
         trip_distance_km: 222
         fuel_relative: 0.5
        }
        profit_log : _nameless.profit.1 {
         acc_distance_on_job: 100
         acc_distance_free: 25
        }
        profit_log : _nameless.profit.2 {
         acc_distance_on_job: 333
         acc_distance_free: 44
        }
        trailer : _nameless.trailer.1 {
         cargo_mass: 10000
         slave_trailer: _nameless.trailer.2
        }
        trailer : _nameless.trailer.2 {
         cargo_mass: 10000
         slave_trailer: null
        }
        trailer : _nameless.trailer.3 {
         cargo_mass: 999
         slave_trailer: null
        }
        }
        """;

    private sealed class NeverRunningGuard : IScsGameProcessGuard
    {
        public bool IsAnyScsGameRunning() => false;
    }
}
