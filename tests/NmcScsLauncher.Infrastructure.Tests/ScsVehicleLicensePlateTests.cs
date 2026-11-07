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

    [Theory]
    [InlineData(GameType.Ets2)]
    [InlineData(GameType.Ats)]
    public async Task InspectReadsActivePlateScalarsWithoutWriting(GameType game)
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        save = save with { GameType = game };
        var before = await File.ReadAllBytesAsync(save.GameSiiPath);
        var state = await new ScsVehicleEditService(codec, saveEdit).InspectActiveVehiclesAsync(save);

        Assert.Equal("\"OLDTRUCK|germany\"", state.TruckLicensePlate);
        Assert.Equal("\"OLDTRAILER1|germany\"", state.TrailerLicensePlate);
        Assert.Equal(before, await File.ReadAllBytesAsync(save.GameSiiPath));
        Assert.False(Directory.Exists(Path.Combine(_root, "backups")));
    }

    [Fact]
    public async Task InspectUsesFirstPlateBearingTrailerWhenHeadHasNoPlate()
    {
        var content = CreateSave().Replace(" license_plate: \"OLDTRAILER1|germany\"", string.Empty);
        var (save, codec, saveEdit) = await CreateAsync(content);
        var state = await new ScsVehicleEditService(codec, saveEdit).InspectActiveVehiclesAsync(save);
        Assert.Equal("\"OLDTRAILER2|germany\"", state.TrailerLicensePlate);
    }

    [Fact]
    public async Task InspectDoesNotInventPlatesForMissingFieldsOrTrailer()
    {
        var content = CreateSave()
            .Replace(" license_plate: \"OLDTRUCK|germany\"", string.Empty)
            .Replace(" trailer: _nameless.trailer.1", " trailer: null");
        var (save, codec, saveEdit) = await CreateAsync(content);
        var state = await new ScsVehicleEditService(codec, saveEdit).InspectActiveVehiclesAsync(save);
        Assert.Null(state.TruckLicensePlate);
        Assert.Null(state.TrailerLicensePlate);
    }

    [Theory]
    [InlineData(GameType.Ets2, "germany", "SI AA 998")]
    [InlineData(GameType.Ets2, "uk", "YN16 MSO")]
    [InlineData(GameType.Ats, "texas", "NMC-42")]
    [InlineData(GameType.Ets2, "custom", "NMC-42")]
    [InlineData(GameType.Ats, "custom", "NMC-42")]
    [InlineData(GameType.Ets2, "promods_region", "NMC-42")]
    [InlineData(GameType.Ats, "mod_state", "NMC-42")]
    public async Task WrittenPlatesReloadWithTheirOriginalSelection(GameType game, string country, string text)
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        save = save with { GameType = game };
        var editor = new ScsVehicleEditService(codec, saveEdit);
        await editor.SetActiveTruckLicensePlateAsync(save, text, country, "12abef", "345678");
        await editor.SetActiveTrailerLicensePlateAsync(save, text, country, "12abef", "345678");
        var state = await editor.InspectActiveVehiclesAsync(save);
        foreach (var scalar in new[] { state.TruckLicensePlate, state.TrailerLicensePlate })
        {
            var plate = Assert.IsType<ScsLicensePlateValue>(ScsLicensePlateValue.Parse(scalar));
            Assert.Equal(text, plate.Text);
            Assert.Equal(country, plate.CountryCode);
            if (country == "custom")
            {
                Assert.Equal("12abef", plate.BackgroundRgb);
                Assert.Equal("345678", plate.TextRgb);
            }
        }
    }

    [Fact]
    public async Task SetTruckLicensePlateUpdatesOnlyActiveTruck()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);

        var result = await editor.SetActiveTruckLicensePlateAsync(
            save,
            "SI AA 998",
            "germany",
            "ffffff",
            "111111");

        Assert.True(File.Exists(result.BackupPath));

        var text = await File.ReadAllTextAsync(save.GameSiiPath);
        Assert.Contains(
            "<font xscale=1.00 yscale=1.25>SI<offset hshift=4 vshift=-2.5><img src=/material/ui/lp/germany/b.mat color=FFFFFFFF><offset hshift=4 vshift=2>AA 998</font>|germany",
            text);
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
            "SI AB 123",
            "germany",
            "ffffff",
            "000000");

        Assert.True(File.Exists(result.BackupPath));

        var text = await File.ReadAllTextAsync(save.GameSiiPath);
        Assert.Equal(
            2,
            CountOccurrences(
                text,
                "<font xscale=1.00 yscale=1.25>SI<offset hshift=4 vshift=-2.5><img src=/material/ui/lp/germany/b.mat color=FFFFFFFF><offset hshift=4 vshift=2>AB 123</font>|germany"));
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
    public async Task GermanPlateRejectsMissingDistrictStructureWithoutWriting()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);
        var before = await File.ReadAllTextAsync(save.GameSiiPath);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            editor.SetActiveTrailerLicensePlateAsync(
                save,
                "NMC-1",
                "germany",
                "ffffff",
                "000000"));

        Assert.Equal(before, await File.ReadAllTextAsync(save.GameSiiPath));
    }

    [Fact]
    public async Task NonGermanCountryUsesNativeTextAndCountryCode()
    {
        var (save, codec, saveEdit) = await CreateAsync(CreateSave());
        var editor = new ScsVehicleEditService(codec, saveEdit);

        await editor.SetActiveTruckLicensePlateAsync(
            save,
            "YN16 MSO",
            "uk",
            "ffffff",
            "000000");

        var text = await File.ReadAllTextAsync(save.GameSiiPath);
        Assert.Contains("license_plate: \"YN16 MSO|uk\"", text);
        Assert.DoesNotContain("/material/ui/white.mat", text);
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
