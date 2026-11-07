using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.App.Tests;

public sealed class SaveEditorPlateWriteLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmationCannotChangeThePlateValuesBeingWritten(bool trailer)
    {
        var harness = new SaveEditorTestHarness();
        var vm = harness.ViewModel;
        var save = SaveEditorTestHarness.Save("confirmed");
        vm.SaveEditorSelectedSave = save;
        var editor = trailer ? vm.SaveEditorTrailerPlate : vm.SaveEditorTruckPlate;
        editor.Load("\"DRAFT 1|map_region\"");
        editor.BackgroundRgb = "123456";
        editor.TextRgb = "abcdef";
        harness.Confirm = (_, message) =>
        {
            Assert.Contains("DRAFT 1", message);
            // A modal dialog pumps the dispatcher: another callback can update bindings here.
            editor.Load(LicensePlateEditorTests.CustomPlate);
            return true;
        };
        object?[]? captured = null;
        harness.VehicleOperation = (_, args) =>
        {
            captured = args;
            return Task.FromException<ScsSaveEditResult>(new ScsSaveEditException("Captured write"));
        };

        await (trailer ? vm.SetSaveEditorTrailerPlateCommand : vm.SetSaveEditorTruckPlateCommand).ExecuteAsync(null);

        Assert.NotNull(captured);
        Assert.Same(save, captured[0]);
        Assert.Equal("DRAFT 1", captured[1]);
        Assert.Equal("map_region", captured[2]);
        Assert.Equal("123456", captured[3]);
        Assert.Equal("abcdef", captured[4]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmationThatChangesSaveAbortsTheWrite(bool trailer)
    {
        var harness = new SaveEditorTestHarness();
        var vm = harness.ViewModel;
        vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("old");
        (trailer ? vm.SaveEditorTrailerPlate : vm.SaveEditorTruckPlate).Load("\"DRAFT 1|uk\"");
        harness.Confirm = (_, _) => { vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("new"); return true; };
        var writes = 0;
        harness.VehicleOperation = (_, _) =>
        {
            ++writes;
            return Task.FromException<ScsSaveEditResult>(new ScsSaveEditException("Unexpected write"));
        };

        await (trailer ? vm.SetSaveEditorTrailerPlateCommand : vm.SetSaveEditorTruckPlateCommand).ExecuteAsync(null);

        Assert.Equal(0, writes);
        Assert.Equal("new", vm.SaveEditorSelectedSave?.DisplayName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulPlateWritePreservesOppositeDraftInSameVehicle(bool trailer)
    {
        var (harness, save) = CreateLoadedHarness();
        var vm = harness.ViewModel;
        var writtenEditor = trailer ? vm.SaveEditorTrailerPlate : vm.SaveEditorTruckPlate;
        var otherEditor = trailer ? vm.SaveEditorTruckPlate : vm.SaveEditorTrailerPlate;
        writtenEditor.Load("\"WRITE 1|uk\"");
        otherEditor.Load("\"OLD 2|france\"");
        otherEditor.SelectedRegion = otherEditor.Regions.Single(region => region.Code == ScsPlateRegionCatalog.OtherCode);
        otherEditor.OtherCountryCode = "draft_region";
        otherEditor.Text = "UNSAVED 2";
        otherEditor.BackgroundRgb = "abcdef";
        otherEditor.TextRgb = "123456";
        harness.VehicleOperation = (_, _) => Task.FromResult(Result(save));

        await (trailer ? vm.SetSaveEditorTrailerPlateCommand : vm.SetSaveEditorTruckPlateCommand).ExecuteAsync(null);

        Assert.Equal("UNSAVED 2", otherEditor.Text);
        Assert.Equal("draft_region", otherEditor.CountryCode);
        Assert.Equal("abcdef", otherEditor.BackgroundRgb);
        Assert.Equal("123456", otherEditor.TextRgb);
        Assert.Equal("OLD 2|france", otherEditor.SavedValue);
        Assert.Equal("SAVED 1", writtenEditor.Text);
        Assert.Equal("uk", writtenEditor.CountryCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulPlateWriteDoesNotRestoreDraftForChangedActiveVehicle(bool trailer)
    {
        var (harness, save) = CreateLoadedHarness();
        var vm = harness.ViewModel;
        var otherEditor = trailer ? vm.SaveEditorTruckPlate : vm.SaveEditorTrailerPlate;
        otherEditor.Load("\"UNSAVED 2|draft_region\"");
        harness.VehicleOperation = (_, _) =>
        {
            harness.Inspect = _ => Task.FromResult(new ScsActiveVehicleState(
                trailer ? "new-truck" : "truck", trailer ? "trailer" : "new-trailer", 1, 0.5m,
                "\"FRESH 3|uk\"", "\"FRESH 3|uk\""));
            return Task.FromResult(Result(save));
        };

        await (trailer ? vm.SetSaveEditorTrailerPlateCommand : vm.SetSaveEditorTruckPlateCommand).ExecuteAsync(null);

        Assert.Equal("FRESH 3", otherEditor.Text);
        Assert.Equal("uk", otherEditor.CountryCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulWriteCannotRestoreDraftAfterLeavingAndReturningToSave(bool trailer)
    {
        var (harness, save) = CreateLoadedHarness();
        var vm = harness.ViewModel;
        var otherEditor = trailer ? vm.SaveEditorTruckPlate : vm.SaveEditorTrailerPlate;
        otherEditor.Load("\"UNSAVED 2|draft_region\"");
        var pending = new TaskCompletionSource<ScsSaveEditResult>();
        harness.VehicleOperation = (_, _) => pending.Task;
        var write = (trailer ? vm.SetSaveEditorTrailerPlateCommand : vm.SetSaveEditorTruckPlateCommand).ExecuteAsync(null);
        vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("other");
        vm.SaveEditorSelectedSave = save;
        pending.SetResult(Result(save));
        await write;

        Assert.Equal("SAVED 1", otherEditor.Text);
        Assert.Equal("uk", otherEditor.CountryCode);
    }

    private static (SaveEditorTestHarness Harness, ScsSaveReference Save) CreateLoadedHarness()
    {
        var save = SaveEditorTestHarness.Save("write");
        var harness = new SaveEditorTestHarness
        {
            FindSaves = _ => Task.FromResult<IReadOnlyList<ScsSaveReference>>([save]),
            Inspect = _ => Task.FromResult(new ScsActiveVehicleState("truck", "trailer", 1, 0.5m,
                "\"SAVED 1|uk\"", "\"SAVED 1|uk\""))
        };
        harness.ViewModel.SaveEditorSelectedProfile = SaveEditorTestHarness.Profile("write");
        return (harness, save);
    }

    private static ScsSaveEditResult Result(ScsSaveReference save) =>
        new(save.GameSiiPath, "synthetic-backup", "plate", "before", "after", DateTimeOffset.UnixEpoch);
}
