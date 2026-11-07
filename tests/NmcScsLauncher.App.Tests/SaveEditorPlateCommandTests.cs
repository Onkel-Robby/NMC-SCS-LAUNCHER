using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.App.Tests;

public sealed class SaveEditorPlateCommandTests
{
    public static IEnumerable<object[]> RegionCases()
    {
        foreach (var game in new[] { GameType.Ets2, GameType.Ats })
        foreach (var region in ScsPlateRegionCatalog.For(game))
        foreach (var trailer in new[] { false, true })
            yield return [game, region.Code, trailer];
    }

    [Theory]
    [MemberData(nameof(RegionCases))]
    public async Task PlateCommandsUseTheCorrectVehiclesTextRegionAndColors(GameType game, string code, bool trailer)
    {
        var harness = new SaveEditorTestHarness();
        var vm = harness.ViewModel;
        vm.SaveEditorSelectedGame = game;
        var save = SaveEditorTestHarness.Save("write", game);
        vm.SaveEditorSelectedSave = save;
        var editor = trailer ? vm.SaveEditorTrailerPlate : vm.SaveEditorTruckPlate;
        var otherEditor = trailer ? vm.SaveEditorTruckPlate : vm.SaveEditorTrailerPlate;
        otherEditor.Load(LicensePlateEditorTests.CustomPlate);
        editor.SelectedRegion = editor.Regions.Single(region => region.Code == code);
        editor.OtherCountryCode = "  map_region_42  ";
        editor.Text = trailer ? "TRAILER 2" : "TRUCK 1";
        editor.BackgroundRgb = "12ab34";
        editor.TextRgb = "98cd76";
        string? operation = null;
        object?[]? captured = null;
        harness.VehicleOperation = (method, args) =>
        {
            operation = method;
            captured = args;
            // Capture at the write boundary; no file or backup is created by this fake.
            return Task.FromException<ScsSaveEditResult>(new ScsSaveEditException("Captured synthetic write"));
        };

        await (trailer ? vm.SetSaveEditorTrailerPlateCommand : vm.SetSaveEditorTruckPlateCommand).ExecuteAsync(null);

        Assert.Equal(trailer ? nameof(IScsVehicleEditService.SetActiveTrailerLicensePlateAsync)
            : nameof(IScsVehicleEditService.SetActiveTruckLicensePlateAsync), operation);
        Assert.NotNull(captured);
        Assert.Same(save, captured[0]);
        Assert.Equal(trailer ? "TRAILER 2" : "TRUCK 1", captured[1]);
        Assert.Equal(code == ScsPlateRegionCatalog.OtherCode ? "map_region_42" : code, captured[2]);
        Assert.Equal("12ab34", captured[3]);
        Assert.Equal("98cd76", captured[4]);
        Assert.Equal("CUSTOM 1", otherEditor.Text);
        Assert.Equal("123abc", otherEditor.BackgroundRgb);
    }
}
