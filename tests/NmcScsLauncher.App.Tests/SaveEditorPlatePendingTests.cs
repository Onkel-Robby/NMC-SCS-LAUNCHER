using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.App.Tests;

public sealed class SaveEditorPlatePendingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PendingInspectionRejectsDirectExecutionBeforeConfirmationOrWrite(bool trailer) => Task.Run(async () =>
    {
        var pending = new TaskCompletionSource<ScsActiveVehicleState>();
        var confirmations = 0;
        var writes = 0;
        var harness = new SaveEditorTestHarness
        {
            Inspect = _ => pending.Task,
            Confirm = (_, _) => { confirmations++; return true; },
            VehicleOperation = (_, _) =>
            {
                writes++;
                return Task.FromException<ScsSaveEditResult>(new ScsSaveEditException("Unexpected write while loading"));
            }
        };
        var vm = harness.ViewModel;
        vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("pending");
        var editor = trailer ? vm.SaveEditorTrailerPlate : vm.SaveEditorTruckPlate;
        editor.Load("\"DRAFT 123|uk\"");

        await (trailer ? vm.SetSaveEditorTrailerPlateCommand : vm.SetSaveEditorTruckPlateCommand).ExecuteAsync(null);

        Assert.Equal(0, confirmations);
        Assert.Equal(0, writes);
        Assert.False(vm.SetSaveEditorTruckPlateCommand.CanExecute(null));
        Assert.False(vm.SetSaveEditorTrailerPlateCommand.CanExecute(null));
        pending.SetResult(LoadedState());
    });

    [Fact]
    public Task CurrentInspectionCompletionEnablesBothCommandsAndNotifiesBindings() => Task.Run(() =>
    {
        var pending = new TaskCompletionSource<ScsActiveVehicleState>();
        var harness = new SaveEditorTestHarness { Inspect = _ => pending.Task };
        var vm = harness.ViewModel;
        var truckNotifications = 0;
        var trailerNotifications = 0;
        vm.SetSaveEditorTruckPlateCommand.CanExecuteChanged += (_, _) => truckNotifications++;
        vm.SetSaveEditorTrailerPlateCommand.CanExecuteChanged += (_, _) => trailerNotifications++;
        vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("pending");

        Assert.False(vm.SetSaveEditorTruckPlateCommand.CanExecute(null));
        Assert.False(vm.SetSaveEditorTrailerPlateCommand.CanExecute(null));
        var truckBeforeCompletion = truckNotifications;
        var trailerBeforeCompletion = trailerNotifications;
        pending.SetResult(LoadedState());

        Assert.True(vm.SetSaveEditorTruckPlateCommand.CanExecute(null));
        Assert.True(vm.SetSaveEditorTrailerPlateCommand.CanExecute(null));
        Assert.True(truckNotifications > truckBeforeCompletion);
        Assert.True(trailerNotifications > trailerBeforeCompletion);
    });

    [Fact]
    public Task StaleInspectionCannotEnableCommandsWhileNewerInspectionIsPending() => Task.Run(() =>
    {
        var oldPending = new TaskCompletionSource<ScsActiveVehicleState>();
        var currentPending = new TaskCompletionSource<ScsActiveVehicleState>();
        var harness = new SaveEditorTestHarness
        {
            Inspect = save => save.DisplayName == "old" ? oldPending.Task : currentPending.Task
        };
        var vm = harness.ViewModel;
        vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("old");
        vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("current");

        oldPending.SetResult(LoadedState());

        Assert.False(vm.SetSaveEditorTruckPlateCommand.CanExecute(null));
        Assert.False(vm.SetSaveEditorTrailerPlateCommand.CanExecute(null));
        Assert.Empty(vm.SaveEditorTruckPlate.Text);
        Assert.Empty(vm.SaveEditorTrailerPlate.Text);
        currentPending.SetResult(LoadedState());
        Assert.True(vm.SetSaveEditorTruckPlateCommand.CanExecute(null));
        Assert.True(vm.SetSaveEditorTrailerPlateCommand.CanExecute(null));
    });

    [Fact]
    public Task ClearingSaveClearsDraftsAndRejectsBothCommands() => Task.Run(async () =>
    {
        var confirmations = 0;
        var writes = 0;
        var harness = new SaveEditorTestHarness
        {
            Inspect = _ => Task.FromResult(LoadedState()),
            Confirm = (_, _) => { confirmations++; return true; },
            VehicleOperation = (_, _) =>
            {
                writes++;
                return Task.FromException<ScsSaveEditResult>(new ScsSaveEditException("Unexpected write without selection"));
            }
        };
        var vm = harness.ViewModel;
        vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("loaded");
        Assert.Equal("TRUCK 1", vm.SaveEditorTruckPlate.Text);
        Assert.Equal("TRAILER 2", vm.SaveEditorTrailerPlate.Text);

        vm.SaveEditorSelectedSave = null;

        Assert.Empty(vm.SaveEditorTruckPlate.Text);
        Assert.Empty(vm.SaveEditorTrailerPlate.Text);
        Assert.False(vm.SetSaveEditorTruckPlateCommand.CanExecute(null));
        Assert.False(vm.SetSaveEditorTrailerPlateCommand.CanExecute(null));
        await vm.SetSaveEditorTruckPlateCommand.ExecuteAsync(null);
        await vm.SetSaveEditorTrailerPlateCommand.ExecuteAsync(null);
        Assert.Equal(0, confirmations);
        Assert.Equal(0, writes);
    });

    [Fact]
    public Task CompletionAfterSelectionWasClearedCannotEnableCommands() => Task.Run(() =>
    {
        var pending = new TaskCompletionSource<ScsActiveVehicleState>();
        var harness = new SaveEditorTestHarness { Inspect = _ => pending.Task };
        var vm = harness.ViewModel;
        vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("pending");
        vm.SaveEditorSelectedSave = null;

        pending.SetResult(LoadedState());

        Assert.Null(vm.SaveEditorSelectedSave);
        Assert.False(vm.SetSaveEditorTruckPlateCommand.CanExecute(null));
        Assert.False(vm.SetSaveEditorTrailerPlateCommand.CanExecute(null));
        Assert.Empty(vm.SaveEditorTruckPlate.Text);
        Assert.Empty(vm.SaveEditorTrailerPlate.Text);
    });

    private static ScsActiveVehicleState LoadedState() =>
        new("truck", "trailer", 1, 0.5m, "\"TRUCK 1|uk\"", "\"TRAILER 2|france\"");
}
