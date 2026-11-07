using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;

namespace NmcScsLauncher.App.ViewModels;

public partial class MainViewModel
{
    private IScsPowertrainDiscoveryService? _powertrainDiscoveryService;
    private ScsActiveTruckPowertrainCatalog? _saveEditorPowertrainCatalog;
    private IReadOnlyList<PowertrainDiscoveryItemViewModel> _allDiscoveredPowertrains = [];
    private CancellationTokenSource? _powertrainDiscoveryCancellation;
    private long _powertrainDiscoveryGeneration;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DiscoverSaveEditorPowertrainCommand), nameof(CancelSaveEditorPowertrainDiscoveryCommand))]
    private bool _saveEditorIsDiscoveringPowertrain;

    [ObservableProperty] private string _saveEditorPowertrainStatusText = "Save auswählen und Komponenten suchen.";
    [ObservableProperty] private string _saveEditorPowertrainDiagnostics = string.Empty;
    [ObservableProperty] private string _saveEditorPowertrainSearch = string.Empty;
    [ObservableProperty] private bool _saveEditorPowertrainOnlyCurrentModel = true;
    [ObservableProperty] private PowertrainDiscoveryItemViewModel? _saveEditorSelectedDiscoveredPowertrain;

    public ObservableCollection<PowertrainDiscoveryItemViewModel> SaveEditorDiscoveredPowertrains { get; } = new();
    public string SaveEditorPowertrainModel => _saveEditorPowertrainCatalog?.TruckModelRoot
        .TrimEnd('/').Split('/').LastOrDefault() ?? "–";
    public string SaveEditorPowertrainResultCount =>
        $"{SaveEditorDiscoveredPowertrains.Count} von {_allDiscoveredPowertrains.Count} Komponenten angezeigt";

    public void ConfigurePowertrainDiscoveryService(IScsPowertrainDiscoveryService service)
    {
        _powertrainDiscoveryService = service ?? throw new ArgumentNullException(nameof(service));
        DiscoverSaveEditorPowertrainCommand.NotifyCanExecuteChanged();
    }

    private bool CanDiscoverSaveEditorPowertrain() => _powertrainDiscoveryService is not null &&
        _saveEditorPowertrainCatalog is not null && SaveEditorSelectedSave is not null &&
        !SaveEditorIsInspecting && !IsBusy && !SaveEditorIsDiscoveringPowertrain;

    [RelayCommand(CanExecute = nameof(CanDiscoverSaveEditorPowertrain), AllowConcurrentExecutions = true)]
    private async Task DiscoverSaveEditorPowertrainAsync()
    {
        if (!CanDiscoverSaveEditorPowertrain()) return;

        var service = _powertrainDiscoveryService!;
        var catalog = _saveEditorPowertrainCatalog!;
        var save = SaveEditorSelectedSave;
        var profile = SaveEditorSelectedProfile;
        var game = SaveEditorSelectedGame;
        var inspectionGeneration = _saveEditorInspectionGeneration;
        var selectionGeneration = _saveEditorSelectionGeneration;
        var generation = ++_powertrainDiscoveryGeneration;
        using var cancellation = new CancellationTokenSource();
        _powertrainDiscoveryCancellation = cancellation;
        var modDirectories = new List<ScsPowertrainModDirectory>();
        var pathDiagnostics = new List<string>();
        foreach (var modset in Modsets.Where(item => item.Game == game))
        {
            try
            {
                modDirectories.Add(new(game, modset.Name, ScsModsetPathResolver.GetModDirectory(modset)));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                pathDiagnostics.Add($"Mod-Ordner '{modset.Name}': ungültiger Pfad; Quelle übersprungen.");
            }
        }
        var request = new ScsPowertrainDiscoveryRequest(
            game, GetSavedPath(game), profile?.GameType == game ? profile.GameHomeDirectory : null,
            catalog, modDirectories.ToArray());
        bool IsCurrent() => generation == _powertrainDiscoveryGeneration &&
            inspectionGeneration == _saveEditorInspectionGeneration &&
            selectionGeneration == _saveEditorSelectionGeneration &&
            game == SaveEditorSelectedGame && ReferenceEquals(save, SaveEditorSelectedSave) &&
            ReferenceEquals(profile, SaveEditorSelectedProfile) &&
            ReferenceEquals(catalog, _saveEditorPowertrainCatalog) && !cancellation.IsCancellationRequested;

        SaveEditorIsDiscoveringPowertrain = true;
        SaveEditorPowertrainStatusText = "Installierte Dateien und lokale Mods werden gelesen …";
        SaveEditorPowertrainDiagnostics = string.Empty;
        try
        {
            // Archive work must never run on the WPF dispatcher or delay plate editing.
            var result = await Task.Run(() => service.DiscoverAsync(request, cancellation.Token), cancellation.Token);
            if (!IsCurrent()) return;
            _allDiscoveredPowertrains = result.Components.Where(item => item.Game == game)
                .OrderByDescending(item => item.IsCurrent).ThenBy(item => item.Kind)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new PowertrainDiscoveryItemViewModel(item)).ToArray();
            ApplySaveEditorPowertrainFilter();
            var diagnostics = pathDiagnostics.Concat(result.Diagnostics).ToArray();
            SaveEditorPowertrainDiagnostics = string.Join(Environment.NewLine, diagnostics);
            SaveEditorPowertrainStatusText =
                $"{_allDiscoveredPowertrains.Count} Komponenten erkannt. Externe Komponenten bleiben schreibgeschützt.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation or a changed context already owns the status message.
        }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            SaveEditorPowertrainStatusText =
                "Komponenten konnten nicht gelesen werden. Die Auswahl aus dem Save bleibt verfügbar.";
            await _logger.WriteAsync("WARN", "Read-only powertrain discovery failed.", ex);
        }
        finally
        {
            if (generation == _powertrainDiscoveryGeneration)
            {
                _powertrainDiscoveryCancellation = null;
                SaveEditorIsDiscoveringPowertrain = false;
            }
        }
    }

    private bool CanCancelSaveEditorPowertrainDiscovery() => SaveEditorIsDiscoveringPowertrain;

    [RelayCommand(CanExecute = nameof(CanCancelSaveEditorPowertrainDiscovery))]
    private void CancelSaveEditorPowertrainDiscovery()
    {
        CancelPowertrainDiscovery();
        SaveEditorPowertrainStatusText = "Komponentensuche abgebrochen. Die Auswahl aus dem Save bleibt verfügbar.";
    }

    private void CancelPowertrainDiscovery()
    {
        ++_powertrainDiscoveryGeneration;
        _powertrainDiscoveryCancellation?.Cancel();
        _powertrainDiscoveryCancellation = null;
        SaveEditorIsDiscoveringPowertrain = false;
    }

    private void ClearSaveEditorPowertrainDiscovery()
    {
        CancelPowertrainDiscovery();
        _saveEditorPowertrainCatalog = null;
        _allDiscoveredPowertrains = [];
        SaveEditorDiscoveredPowertrains.Clear();
        SaveEditorSelectedDiscoveredPowertrain = null;
        SaveEditorPowertrainDiagnostics = string.Empty;
        SaveEditorPowertrainStatusText = "Save auswählen und Komponenten suchen.";
        OnPropertyChanged(nameof(SaveEditorPowertrainModel));
        OnPropertyChanged(nameof(SaveEditorPowertrainResultCount));
        NotifySaveEditorPowertrainAvailability();
    }

    private void SetSaveEditorPowertrainCatalog(ScsActiveTruckPowertrainCatalog catalog)
    {
        // Keep a private snapshot; mutable UI collections cannot extend write authorization.
        _saveEditorPowertrainCatalog = catalog with
        {
            Engines = catalog.Engines.ToArray(),
            Transmissions = catalog.Transmissions.ToArray()
        };
        SaveEditorPowertrainStatusText = "Komponenten aus Spiel, DLCs und lokalen Mods können jetzt gesucht werden.";
        OnPropertyChanged(nameof(SaveEditorPowertrainModel));
        NotifySaveEditorPowertrainAvailability();
    }

    partial void OnSaveEditorPowertrainSearchChanged(string value) => ApplySaveEditorPowertrainFilter();
    partial void OnSaveEditorPowertrainOnlyCurrentModelChanged(bool value) => ApplySaveEditorPowertrainFilter();

    private void ApplySaveEditorPowertrainFilter()
    {
        var selected = SaveEditorSelectedDiscoveredPowertrain;
        var query = SaveEditorPowertrainSearch.Trim();
        var filtered = _allDiscoveredPowertrains.Where(item =>
            (!SaveEditorPowertrainOnlyCurrentModel || string.Equals(item.TruckModel, SaveEditorPowertrainModel, StringComparison.OrdinalIgnoreCase)) &&
            (query.Length == 0 || item.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToArray();
        SaveEditorDiscoveredPowertrains.Clear();
        foreach (var item in filtered) SaveEditorDiscoveredPowertrains.Add(item);
        SaveEditorSelectedDiscoveredPowertrain = selected is not null && filtered.Contains(selected)
            ? selected : filtered.FirstOrDefault();
        OnPropertyChanged(nameof(SaveEditorPowertrainResultCount));
    }

    private void NotifySaveEditorPowertrainAvailability()
    {
        SetSaveEditorEngineCommand.NotifyCanExecuteChanged();
        SetSaveEditorTransmissionCommand.NotifyCanExecuteChanged();
        DiscoverSaveEditorPowertrainCommand.NotifyCanExecuteChanged();
    }

    partial void OnSaveEditorIsInspectingChanged(bool value) => NotifySaveEditorPowertrainAvailability();
    partial void OnSaveEditorSelectedEngineChanged(ScsPowertrainCatalogItem? value) => SetSaveEditorEngineCommand.NotifyCanExecuteChanged();
    partial void OnSaveEditorSelectedTransmissionChanged(ScsPowertrainCatalogItem? value) => SetSaveEditorTransmissionCommand.NotifyCanExecuteChanged();

    private bool CanSetSaveEditorEngine() => CanSetSaveEditorPowertrain(false);
    private bool CanSetSaveEditorTransmission() => CanSetSaveEditorPowertrain(true);
    private bool CanSetSaveEditorPowertrain(bool transmission)
    {
        if (!SaveEditorCanEditPlates || _saveEditorPowertrainCatalog is null) return false;
        var selected = transmission ? SaveEditorSelectedTransmission : SaveEditorSelectedEngine;
        var choices = transmission ? _saveEditorPowertrainCatalog.Transmissions : _saveEditorPowertrainCatalog.Engines;
        return selected is not null && choices.Any(item => ReferenceEquals(item, selected));
    }

    private async Task SetSaveEditorPowertrainAsync(bool transmission)
    {
        if (!CanSetSaveEditorPowertrain(transmission)) return;
        var selected = transmission ? SaveEditorSelectedTransmission! : SaveEditorSelectedEngine!;
        var save = SaveEditorSelectedSave!;
        var profile = SaveEditorSelectedProfile;
        var game = SaveEditorSelectedGame;
        var selectionGeneration = _saveEditorSelectionGeneration;
        var inspectionGeneration = _saveEditorInspectionGeneration;
        var profileGeneration = _saveEditorProfileLoadGeneration;
        var catalog = _saveEditorPowertrainCatalog;
        var title = transmission ? "Getriebe ändern" : "Motor ändern";

        await ExecuteSelectedVehicleEditAsync(
            title, $"{(transmission ? "Getriebe" : "Motor")} auf '{selected.DataPath}' setzen?",
            () => transmission
                ? _saveEditorVehicleEditService!.SetActiveTruckTransmissionAsync(save, selected.DataPath)
                : _saveEditorVehicleEditService!.SetActiveTruckEngineAsync(save, selected.DataPath),
            () => game == SaveEditorSelectedGame && selectionGeneration == _saveEditorSelectionGeneration &&
                inspectionGeneration == _saveEditorInspectionGeneration && profileGeneration == _saveEditorProfileLoadGeneration &&
                ReferenceEquals(save, SaveEditorSelectedSave) && ReferenceEquals(profile, SaveEditorSelectedProfile) &&
                ReferenceEquals(catalog, _saveEditorPowertrainCatalog) &&
                ReferenceEquals(selected, transmission ? SaveEditorSelectedTransmission : SaveEditorSelectedEngine) &&
                CanSetSaveEditorPowertrain(transmission));
    }
}

public sealed class PowertrainDiscoveryItemViewModel(ScsDiscoveredPowertrain component)
{
    public ScsDiscoveredPowertrain Component { get; } = component;
    public string Name => Component.Name;
    public string TruckModel => Component.TruckModel;
    public string DataPath => Component.DataPath;
    public bool IsCurrent => Component.IsCurrent;
    public string CurrentText => IsCurrent ? "Aktuell verbaut" : "Nicht verbaut";
    public string KindText => Component.Kind == ScsPowertrainKind.Engine ? "Motor" : "Getriebe";
    public string CompatibilityText => Component.Compatibility switch
    {
        ScsPowertrainCompatibility.ObservedInSave => "Im Save verwendet",
        ScsPowertrainCompatibility.ModelMatchUnverified => "Nicht verifiziert",
        ScsPowertrainCompatibility.DifferentModel => "Anderes Truck-Modell",
        ScsPowertrainCompatibility.Ambiguous => "Mehrdeutig / Konflikt",
        _ => "Unvollständig geprüft"
    };
    public string CompatibilityReason => Component.CompatibilityReason;
    public string SourceText => string.Join(", ", Component.Sources.Select(source => SourceKindText(source.Kind)).Distinct());
    public string SourceDetails => string.Join(Environment.NewLine, Component.Sources.Select(source =>
        $"{SourceKindText(source.Kind)} · {source.Name}{Environment.NewLine}{source.Location}"));
    public string SearchText => $"{Name} {TruckModel} {DataPath} {KindText} {CompatibilityText} {SourceDetails}";

    private static string SourceKindText(ScsPowertrainSourceKind kind) => kind switch
    {
        ScsPowertrainSourceKind.Save => "Spielstand",
        ScsPowertrainSourceKind.Game => "Spiel",
        ScsPowertrainSourceKind.Dlc => "DLC-Datei",
        ScsPowertrainSourceKind.LocalMod => "Lokaler Mod",
        ScsPowertrainSourceKind.Workshop => "Workshop",
        _ => "Unbekannt"
    };
}
