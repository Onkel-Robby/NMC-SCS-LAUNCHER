using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.App.Tests;

public sealed class SaveEditorPlateStateTests
{
    [Theory]
    [InlineData(GameType.Ets2, "uk")]
    [InlineData(GameType.Ats, "texas")]
    public void LoadedActiveTruckAndTrailerKeepIndependentRegions(GameType game, string nativeCode)
    {
        var harness = new SaveEditorTestHarness
        {
            Inspect = _ => Task.FromResult(new ScsActiveVehicleState("truck", "trailer", 1, 0.5m,
                $"\"ACTIVE 1|{nativeCode}\"", LicensePlateEditorTests.CustomPlate))
        };
        var vm = harness.ViewModel;
        vm.SaveEditorSelectedGame = game;
        vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("active", game);

        Assert.Equal("ACTIVE 1", vm.SaveEditorTruckPlate.Text);
        Assert.Equal(nativeCode, vm.SaveEditorTruckPlate.CountryCode);
        Assert.Equal("custom", vm.SaveEditorTrailerPlate.CountryCode);
        Assert.Equal("123abc", vm.SaveEditorTrailerPlate.BackgroundRgb);
        vm.SaveEditorSelectedTruck = new("inactive-truck", 1, false, "Inactive");
        vm.SaveEditorSelectedTrailer = new("inactive-trailer", 1, false, "Inactive");
        Assert.Equal("ACTIVE 1", vm.SaveEditorTruckPlate.Text);
        Assert.Equal("CUSTOM 1", vm.SaveEditorTrailerPlate.Text);

        vm.SaveEditorSelectedSave = null;
        Assert.Empty(vm.SaveEditorTruckPlate.Text);
        Assert.Empty(vm.SaveEditorTrailerPlate.Text);
        Assert.Empty(vm.SaveEditorTruckPlate.CountryCode);
        Assert.Empty(vm.SaveEditorTrailerPlate.CountryCode);
    }

    [Theory]
    [InlineData(GameType.Ets2, GameType.Ats, "california", "germany")]
    [InlineData(GameType.Ats, GameType.Ets2, "germany", "california")]
    public void GameSwitchClearsOtherCountryDraft(GameType from, GameType to, string nativeCode, string foreignCode)
    {
        var vm = new SaveEditorTestHarness().ViewModel;
        vm.SaveEditorSelectedGame = from;
        vm.SaveEditorTruckPlate.OtherCountryCode = "promods_region";
        vm.SaveEditorTruckPlate.SelectedRegion = vm.SaveEditorTruckPlate.Regions.Single(item => item.Code == ScsPlateRegionCatalog.OtherCode);

        vm.SaveEditorSelectedGame = to;

        Assert.Empty(vm.SaveEditorTruckPlate.OtherCountryCode);
        Assert.Null(vm.SaveEditorTruckPlate.SelectedRegion);
        Assert.Contains(vm.SaveEditorTruckPlate.Regions, item => item.Code == nativeCode);
        Assert.DoesNotContain(vm.SaveEditorTruckPlate.Regions, item => item.Code == foreignCode);
    }

    [Fact]
    public void ProfileChangeClearsPlateDraft()
    {
        var vm = new SaveEditorTestHarness().ViewModel;
        vm.SaveEditorTruckPlate.Text = "OLD 123";
        vm.SaveEditorTrailerPlate.Text = "OLD 456";

        vm.SaveEditorSelectedProfile = SaveEditorTestHarness.Profile("next");

        Assert.Empty(vm.SaveEditorTruckPlate.Text);
        Assert.Empty(vm.SaveEditorTrailerPlate.Text);
    }

    [Fact]
    public Task DelayedProfileResultsCannotRepopulateAnotherGame() => Task.Run(() =>
    {
        var pending = new TaskCompletionSource<IReadOnlyList<ScsSaveReference>>();
        var harness = new SaveEditorTestHarness { FindSaves = _ => pending.Task };
        var vm = harness.ViewModel;
        vm.SaveEditorSelectedProfile = SaveEditorTestHarness.Profile("old");

        vm.SaveEditorSelectedGame = GameType.Ats;
        pending.SetResult([SaveEditorTestHarness.Save("old")]);

        Assert.Empty(vm.SaveEditorSaves);
        Assert.Null(vm.SaveEditorSelectedSave);
        Assert.Empty(vm.SaveEditorTrucks);
    });

    [Fact]
    public Task DelayedInspectionCannotOverwriteNewerSave() => Task.Run(() =>
    {
        var pending = new TaskCompletionSource<ScsActiveVehicleState>();
        var harness = new SaveEditorTestHarness
        {
            Inspect = save => save.DisplayName == "old" ? pending.Task
                : Task.FromResult(new ScsActiveVehicleState("new", null, 0, 0.5m))
        };
        var vm = harness.ViewModel;
        vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("old");
        vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("new");

        pending.SetResult(new ScsActiveVehicleState("old", null, 0, 0.1m));

        Assert.Equal("new", vm.SaveEditorActiveTruckText);
        Assert.Single(vm.SaveEditorTrucks);
        Assert.Equal("new", vm.SaveEditorSelectedTruck?.Id);
    });

    [Fact]
    public async Task DelayedDiscoveryCannotRestoreProfilesFromPreviousGame()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<ScsProfileReference>>();
        var harness = new SaveEditorTestHarness { FindCloudProfiles = _ => pending.Task };
        var vm = harness.ViewModel;
        var refresh = vm.RefreshSaveEditorCommand.ExecuteAsync(null);
        vm.SaveEditorSelectedGame = GameType.Ats;
        pending.SetResult([SaveEditorTestHarness.Profile("old")]);
        await refresh;

        Assert.Empty(vm.SaveEditorProfiles);
        Assert.Null(vm.SaveEditorSelectedProfile);
    }

    [Fact]
    public async Task DelayedReloadAfterWriteCannotRestorePreviousGameSave()
    {
        var save = SaveEditorTestHarness.Save("old");
        var pending = new TaskCompletionSource<IReadOnlyList<ScsSaveReference>>();
        var harness = new SaveEditorTestHarness { FindSaves = _ => Task.FromResult<IReadOnlyList<ScsSaveReference>>([save]) };
        var vm = harness.ViewModel;
        vm.SaveEditorSelectedProfile = SaveEditorTestHarness.Profile("old");
        harness.FindSaves = _ => pending.Task;
        harness.VehicleOperation = (_, _) => Task.FromResult(new ScsSaveEditResult(
            save.GameSiiPath, "synthetic-backup", "plate", "before", "after", DateTimeOffset.UnixEpoch));
        vm.SaveEditorTruckPlate.Text = "OLD 123";
        vm.SaveEditorTruckPlate.SelectedRegion = vm.SaveEditorTruckPlate.Regions.Single(region => region.Code == "uk");
        var write = vm.SetSaveEditorTruckPlateCommand.ExecuteAsync(null);
        vm.SaveEditorSelectedGame = GameType.Ats;
        pending.SetResult([save]);
        await write;

        Assert.Empty(vm.SaveEditorSaves);
        Assert.Null(vm.SaveEditorSelectedSave);
        Assert.Empty(vm.SaveEditorTruckPlate.CountryCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwitchingActiveVehicleReloadsItsPlate(bool trailer)
    {
        var save = SaveEditorTestHarness.Save("switch");
        var raw = "\"OLD 1|uk\"";
        var harness = new SaveEditorTestHarness
        {
            FindSaves = _ => Task.FromResult<IReadOnlyList<ScsSaveReference>>([save]),
            Inspect = _ => Task.FromResult(new ScsActiveVehicleState("truck", "trailer", 1, 0.5m, raw, raw))
        };
        var vm = harness.ViewModel;
        vm.SaveEditorSelectedProfile = SaveEditorTestHarness.Profile("switch");
        harness.VehicleOperation = (method, _) =>
        {
            Assert.Equal(trailer ? nameof(IScsVehicleEditService.SwitchActiveTrailerAsync)
                : nameof(IScsVehicleEditService.SwitchActiveTruckAsync), method);
            raw = "\"NEW 2|mod_region\"";
            return Task.FromResult(new ScsSaveEditResult(save.GameSiiPath, "synthetic-backup", "switch", "before", "after", DateTimeOffset.UnixEpoch));
        };
        if (trailer)
        {
            vm.SaveEditorSelectedTrailer = new("another-trailer", 1, false);
            await vm.SwitchSaveEditorTrailerCommand.ExecuteAsync(null);
        }
        else
        {
            vm.SaveEditorSelectedTruck = new("another-truck", 1, false);
            await vm.SwitchSaveEditorTruckCommand.ExecuteAsync(null);
        }

        var editor = trailer ? vm.SaveEditorTrailerPlate : vm.SaveEditorTruckPlate;
        Assert.Equal("NEW 2", editor.Text);
        Assert.Equal("mod_region", editor.CountryCode);
        Assert.True(editor.UsesOtherCountry);
    }
}
