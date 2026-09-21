using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsVehicleLicensePlateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NmcScsLauncher-LicensePlateTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SetTruckLicensePlateUpdatesOnlyActiveTruck()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);

        var result = await editor.SetActiveTruckLicensePlateAsync(
            save,
            "NMC-123",
            "germany",
            "ffffff",
            "111111");

        Assert.True(File.Exists(result.BackupPath));

        var text = await File.ReadAllTextAsync(save.GameSiiPath);
        Assert.Contains("NMC-123</align></margin>|germany", text);
        Assert.Contains(" license_plate: \"OTHER|germany\"", text);
        Assert.DoesNotContain(" license_plate: \"OLDTRUCK|germany\"", text);
    }

    [Fact]
    public async Task SetTrailerLicensePlateUpdatesActiveTrailerChainOnly()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);

        var result = await editor.SetActiveTrailerLicensePlateAsync(
            save,
            "NMC-TRAILER",
            "germany",
            "ffffff",
            "000000");

        Assert.True(File.Exists(result.BackupPath));

        var text = await File.ReadAllTextAsync(save.GameSiiPath);
        Assert.Equal(
            2,
            CountOccurrences(text, "NMC-TRAILER</align></margin>|germany"));
        Assert.Contains(" license_plate: \"OTHERTRAILER|germany\"", text);
    }

    [Fact]
    public async Task InvalidPlateCharactersAreRejectedWithoutWriting()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);
        var before = await File.ReadAllTextAsync(save.GameSiiPath);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            editor.SetActiveTruckLicensePlateAsync(
                save,
                "NMC<123",
                "germany",
                "ffffff",
                "000000"));

        Assert.Equal(before, await File.ReadAllTextAsync(save.GameSiiPath));
    }

    [Fact]
    public async Task InvalidRgbIsRejectedWithoutWriting()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);
        var before = await File.ReadAllTextAsync(save.GameSiiPath);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            editor.SetActiveTrailerLicensePlateAsync(
                save,
                "NMC-1",
                "germany",
                "FFFFFG",
                "000000"));

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
        vehicle : _nameless.truck.1 {
         license_plate: "OLDTRUCK|germany"
        }
        vehicle : _nameless.truck.2 {
         license_plate: "OTHER|germany"
        }
        trailer : _nameless.trailer.1 {
         license_plate: "OLDTRAILER1|germany"
         slave_trailer: _nameless.trailer.2
        }
        trailer : _nameless.trailer.2 {
         license_plate: "OLDTRAILER2|germany"
         slave_trailer: null
        }
        trailer : _nameless.trailer.3 {
         license_plate: "OTHERTRAILER|germany"
         slave_trailer: null
        }
        }
        """;

    private sealed class NeverRunningGuard : IScsGameProcessGuard
    {
        public bool IsAnyScsGameRunning() => false;
    }
}
