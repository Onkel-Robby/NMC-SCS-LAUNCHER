using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.ViewModels;

public partial class MainViewModel
{
    private LauncherFeatureFlags _featureFlags = LauncherFeatureFlags.Current;

    public bool PowertrainFeaturesEnabled => _featureFlags.PowertrainEditingEnabled;

    // Instance-local test configuration; production always uses the central default.
    internal void ConfigureFeatureFlags(LauncherFeatureFlags featureFlags)
    {
        _featureFlags = featureFlags ?? throw new ArgumentNullException(nameof(featureFlags));
        _truckPowertrainInstallCancellation?.Cancel();
        ClearSaveEditorPowertrainDiscovery();
        SaveEditorEnginePath = string.Empty;
        SaveEditorTransmissionPath = string.Empty;
        SaveEditorEngines.Clear();
        SaveEditorTransmissions.Clear();
        SaveEditorSelectedEngine = null;
        SaveEditorSelectedTransmission = null;
        OnPropertyChanged(nameof(PowertrainFeaturesEnabled));
        RefreshTruckPowertrainCommand.NotifyCanExecuteChanged();
        CancelSaveEditorPowertrainDiscoveryCommand.NotifyCanExecuteChanged();
        ExportTruckPowertrainDiagnosticsCommand.NotifyCanExecuteChanged();
    }
}
