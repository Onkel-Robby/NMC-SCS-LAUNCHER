using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.App.Tests;

public sealed partial class TruckPowertrainSelectionTests
{
    [Fact]
    public async Task RetainedCommandsAndForgedSelectionsCannotReactivateDisabledFeature()
    {
        var (harness, service) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        var installer = new Installer();
        ConfigureInstaller(vm, installer, new ProcessGuard());
        SelectChangedPair(vm);
        var oldInstall = vm.InstallTruckPowertrainCommand;
        var oldRefresh = vm.RefreshTruckPowertrainCommand;
        var oldEngine = vm.TruckPowertrainSelectedEngine;
        var oldTransmission = vm.TruckPowertrainSelectedTransmission;
        Assert.True(oldInstall.CanExecute(null));
        var calls = service.Requests.Count;
        var confirmations = 0;
        harness.Confirm = (_, _) => { confirmations++; return true; };

        vm.ConfigureFeatureFlags(new LauncherFeatureFlags(false));
        vm.TruckPowertrainSelectedEngine = oldEngine;
        vm.TruckPowertrainSelectedTransmission = oldTransmission;
        vm.TruckPowertrainPairVerified = true;
        Assert.False(oldInstall.CanExecute(null));
        Assert.False(oldRefresh.CanExecute(null));
        oldRefresh.Execute(null);
        await oldInstall.ExecuteAsync(null);
        await vm.DiscoverSaveEditorPowertrainCommand.ExecuteAsync(null);
        await vm.ExportTruckPowertrainDiagnosticsCommand.ExecuteAsync(null);

        Assert.Empty(installer.Requests);
        Assert.Equal(0, confirmations);
        Assert.Equal(calls, service.Requests.Count);
        Assert.Empty(vm.TruckPowertrainEngines);
        Assert.Empty(vm.SaveEditorEngines);
    }

    [Fact]
    public async Task DisablingFeatureDuringConfirmationPreventsInstallerInvocation()
    {
        var (harness, _) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        var installer = new Installer();
        ConfigureInstaller(vm, installer, new ProcessGuard());
        SelectChangedPair(vm);
        harness.Confirm = (_, _) =>
        {
            vm.ConfigureFeatureFlags(new LauncherFeatureFlags(false));
            return true;
        };

        await vm.InstallTruckPowertrainCommand.ExecuteAsync(null);
        Assert.Empty(installer.Requests);
        Assert.False(vm.PowertrainFeaturesEnabled);
    }

    [Fact]
    public async Task DefaultFeatureBlocksCommandsAndBackgroundInspectionWithoutAffectingVehicleLoad()
    {
        var harness = new SaveEditorTestHarness();
        var reads = 0;
        harness.Powertrain = _ => { reads++; throw new InvalidOperationException("Disabled read"); };
        harness.PowertrainCatalog = _ => { reads++; throw new InvalidOperationException("Disabled catalog"); };
        harness.DiscoverPowertrain = (_, _) => { reads++; throw new InvalidOperationException("Disabled discovery"); };
        var service = new Service();
        var vm = harness.ViewModel;
        vm.ConfigureTruckPowertrainService(service);
        var export = new DiagnosticExport { Destination = "must-not-write.json" };
        vm.ConfigureTruckPowertrainDiagnosticExport(export);
        vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("truck");
        for (var i = 0; i < 500 && vm.SaveEditorIsInspecting; i++) await Task.Delay(10);

        Assert.False(vm.PowertrainFeaturesEnabled);
        Assert.False(vm.RefreshTruckPowertrainCommand.CanExecute(null));
        vm.RefreshTruckPowertrainCommand.Execute(null);
        Assert.False(vm.CancelSaveEditorPowertrainDiscoveryCommand.CanExecute(null));
        vm.CancelSaveEditorPowertrainDiscoveryCommand.Execute(null);
        IAsyncRelayCommand[] commands = [vm.DiscoverSaveEditorPowertrainCommand,
            vm.SetSaveEditorEngineCommand, vm.SetSaveEditorTransmissionCommand,
            vm.InstallTruckPowertrainCommand, vm.ExportTruckPowertrainDiagnosticsCommand];
        foreach (var command in commands)
        {
            Assert.False(command.CanExecute(null));
            await command.ExecuteAsync(null); // Bypass CanExecute as an old caller can.
        }
        Assert.Equal(0, reads);
        Assert.Empty(service.Requests);
        Assert.Equal(0, export.DialogCalls);
        Assert.Empty(export.Writes);
        Assert.Empty(vm.TruckPowertrainEngines);
        Assert.Empty(vm.SaveEditorDiscoveredPowertrains);
        Assert.NotEmpty(vm.SaveEditorTrucks);
        Assert.Equal("12", vm.SaveEditorMileageText);
    }
}
