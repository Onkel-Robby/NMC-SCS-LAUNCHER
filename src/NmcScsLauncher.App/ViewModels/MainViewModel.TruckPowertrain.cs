using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;

namespace NmcScsLauncher.App.ViewModels;

public partial class MainViewModel
{
    private IScsTruckPowertrainService? _truckPowertrainService;
    private IScsTruckPowertrainPreview? _truckPowertrainPreview;
    private TruckPowertrainOptionViewModel[] _truckEngineOptions = [];
    private TruckPowertrainOptionViewModel[] _truckTransmissionOptions = [];
    private CancellationTokenSource? _truckPowertrainCancellation;
    private Func<bool>? _truckPowertrainContextMatches;
    private long _truckPowertrainGeneration;
    private bool _truckPowertrainEventsWired;

    [ObservableProperty] private bool _truckPowertrainIsLoading;
    [ObservableProperty] private string _truckPowertrainStatus = "Spielstand mit aktivem Truck auswählen.";
    [ObservableProperty] private string _truckPowertrainContext = string.Empty;
    [ObservableProperty] private string _truckPowertrainDiagnostics = string.Empty;
    [ObservableProperty] private string _truckPowertrainEngineSearch = string.Empty;
    [ObservableProperty] private string _truckPowertrainTransmissionSearch = string.Empty;
    [ObservableProperty] private TruckPowertrainOptionViewModel? _truckPowertrainSelectedEngine;
    [ObservableProperty] private TruckPowertrainOptionViewModel? _truckPowertrainSelectedTransmission;
    [ObservableProperty] private bool _truckPowertrainPairVerified;
    [ObservableProperty] private string _truckPowertrainPairStatus = "Motor und Getriebe auswählen.";

    public ObservableCollection<TruckPowertrainOptionViewModel> TruckPowertrainEngines { get; } = new();
    public ObservableCollection<TruckPowertrainOptionViewModel> TruckPowertrainTransmissions { get; } = new();
    public string TruckPowertrainEngineCount => $"{TruckPowertrainEngines.Count} Motoren";
    public string TruckPowertrainTransmissionCount => $"{TruckPowertrainTransmissions.Count} Getriebe";

    public void ConfigureTruckPowertrainService(IScsTruckPowertrainService service)
    {
        _truckPowertrainService = service ?? throw new ArgumentNullException(nameof(service));
        if (!_truckPowertrainEventsWired)
        {
            PropertyChanged += TruckPowertrainContextChanged;
            _truckPowertrainEventsWired = true;
        }
        ClearTruckPowertrainPreview();
        QueueTruckPowertrainInspection();
    }

    private void TruckPowertrainContextChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SaveEditorSelectedGame) or nameof(SaveEditorSelectedProfile) or
            nameof(SaveEditorSelectedSave) or nameof(SaveEditorSelectedTruck) or nameof(SelectedModset) or
            nameof(IsBusy) or nameof(SaveEditorIsInspecting))
            QueueTruckPowertrainInspection();
    }

    [RelayCommand]
    private void RefreshTruckPowertrain()
    {
        ClearTruckPowertrainPreview();
        QueueTruckPowertrainInspection();
    }

    private void QueueTruckPowertrainInspection()
    {
        if (_truckPowertrainService is null) return;
        if (IsBusy || SaveEditorIsInspecting || _saveEditorPowertrainCatalog is null ||
            SaveEditorSelectedSave is null || SaveEditorSelectedProfile is null ||
            SaveEditorSelectedTruck is not { IsActive: true } ||
            SaveEditorSelectedSave.GameType != SaveEditorSelectedGame || SaveEditorSelectedProfile.GameType != SaveEditorSelectedGame)
        {
            ClearTruckPowertrainPreview();
            if (SaveEditorSelectedTruck is { IsActive: false })
                TruckPowertrainStatus = "Den ausgewählten Truck zuerst mit „Wechseln“ aktivieren.";
            else if (IsBusy || SaveEditorIsInspecting)
                TruckPowertrainStatus = "Die Komponenten werden nach dem Laden des Trucks geprüft.";
            return;
        }
        if (_truckPowertrainContextMatches?.Invoke() == true && (TruckPowertrainIsLoading || _truckPowertrainPreview is not null)) return;
        ClearTruckPowertrainPreview();

        var profile = SaveEditorSelectedProfile;
        var save = SaveEditorSelectedSave;
        var truck = SaveEditorSelectedTruck;
        var modset = SelectedModset;
        var game = SaveEditorSelectedGame;
        var installPath = GetSavedPath(game);
        var catalog = _saveEditorPowertrainCatalog;
        var inspectionGeneration = _saveEditorInspectionGeneration;
        var selectionGeneration = _saveEditorSelectionGeneration;
        var generation = _truckPowertrainGeneration;
        var cancellation = new CancellationTokenSource();
        _truckPowertrainCancellation = cancellation;
        bool Current() => generation == _truckPowertrainGeneration && !cancellation.IsCancellationRequested &&
            !IsBusy && !SaveEditorIsInspecting && game == SaveEditorSelectedGame &&
            string.Equals(installPath, GetSavedPath(game), StringComparison.OrdinalIgnoreCase) &&
            inspectionGeneration == _saveEditorInspectionGeneration && selectionGeneration == _saveEditorSelectionGeneration &&
            ReferenceEquals(profile, SaveEditorSelectedProfile) && ReferenceEquals(save, SaveEditorSelectedSave) &&
            ReferenceEquals(truck, SaveEditorSelectedTruck) && ReferenceEquals(modset, SelectedModset) &&
            ReferenceEquals(catalog, _saveEditorPowertrainCatalog);
        _truckPowertrainContextMatches = Current;
        ScsEffectiveContentRequest request;
        try
        {
            var root = Path.Combine(Path.GetFullPath(profile.GameHomeDirectory), "mod");
            var label = "Mod-Ordner des ausgewählten Profils";
            if (modset?.Game == game)
            {
                var launchHome = ScsModsetPathResolver.UsesDirectModDirectory(modset)
                    ? ScsModsetPathResolver.GetStandardGameDataDirectory(game)
                    : ScsModsetPathResolver.GetRuntimeGameDataDirectory(modset);
                if (SameTruckPowertrainPath(profile.GameHomeDirectory, launchHome))
                {
                    root = ScsModsetPathResolver.GetModDirectory(modset);
                    label = "Start-Modset: " + modset.Name;
                }
            }
            TruckPowertrainContext = label + Environment.NewLine + root;
            request = new(profile, save, installPath, [new(game, label, root)]);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            cancellation.Dispose();
            _truckPowertrainCancellation = null;
            TruckPowertrainStatus = "Der Mod-Ordner konnte nicht eindeutig bestimmt werden.";
            TruckPowertrainDiagnostics = exception.Message;
            return;
        }
        TruckPowertrainIsLoading = true;
        TruckPowertrainStatus = "Motoren, Getriebe und Zubehörregeln werden geprüft …";
        _ = InspectTruckPowertrainAsync(request, truck.Id, catalog.TruckModelRoot, generation, cancellation, Current);
    }

    private async Task InspectTruckPowertrainAsync(ScsEffectiveContentRequest request, string truckId,
        string modelRoot, long generation, CancellationTokenSource cancellation, Func<bool> current)
    {
        try
        {
            var service = _truckPowertrainService!;
            var preview = await Task.Run(() => service.InspectAsync(request, cancellation.Token), cancellation.Token);
            if (!current()) return;
            if (preview.Snapshot.Game != request.Profile.GameType ||
                !SameTruckPowertrainPath(preview.Snapshot.SaveFilePath, request.Save.GameSiiPath) ||
                preview.Snapshot.TruckId != truckId ||
                !string.Equals(preview.Snapshot.TruckModelRoot.TrimEnd('/'), modelRoot.TrimEnd('/'), StringComparison.Ordinal))
                throw new ScsSaveEditException("Die Vorschau gehört nicht mehr zum ausgewählten Truck. Bitte den Spielstand neu laden.");
            _truckPowertrainPreview = preview;
            _truckEngineOptions = Options(preview.Engines, ScsPowertrainKind.Engine);
            _truckTransmissionOptions = Options(preview.Transmissions, ScsPowertrainKind.Transmission);
            ApplyTruckPowertrainFilters();
            TruckPowertrainSelectedEngine = _truckEngineOptions.FirstOrDefault(item => item.IsCurrent);
            TruckPowertrainSelectedTransmission = _truckTransmissionOptions.FirstOrDefault(item => item.IsCurrent);
            TruckPowertrainDiagnostics = string.Join(Environment.NewLine, preview.Diagnostics);
            TruckPowertrainStatus = $"Vorschau · {_truckEngineOptions.Length} Motoren und {_truckTransmissionOptions.Length} Getriebe. Änderungen werden noch nicht gespeichert.";
            AssessTruckPowertrainPair();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!current()) return;
            TruckPowertrainStatus = "Die Komponenten konnten nicht vollständig geprüft werden.";
            TruckPowertrainDiagnostics = exception.Message;
            await _logger.WriteAsync("WARN", "Truck powertrain preview failed.", exception);
        }
        finally
        {
            if (generation == _truckPowertrainGeneration)
            {
                _truckPowertrainCancellation = null;
                TruckPowertrainIsLoading = false;
            }
            cancellation.Dispose();
        }
    }

    private static TruckPowertrainOptionViewModel[] Options(IReadOnlyList<ScsTruckPowertrainOption> options, ScsPowertrainKind kind) =>
        options.Where(option => option.Kind == kind).OrderByDescending(option => option.IsCurrent)
            .ThenBy(option => option.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(option => new TruckPowertrainOptionViewModel(option)).ToArray();

    private void ClearTruckPowertrainPreview()
    {
        ++_truckPowertrainGeneration;
        _truckPowertrainCancellation?.Cancel();
        _truckPowertrainCancellation = null;
        _truckPowertrainContextMatches = null;
        _truckPowertrainPreview = null;
        _truckEngineOptions = [];
        _truckTransmissionOptions = [];
        TruckPowertrainEngineSearch = string.Empty;
        TruckPowertrainTransmissionSearch = string.Empty;
        TruckPowertrainEngines.Clear();
        TruckPowertrainTransmissions.Clear();
        TruckPowertrainSelectedEngine = null;
        TruckPowertrainSelectedTransmission = null;
        TruckPowertrainIsLoading = false;
        TruckPowertrainStatus = "Spielstand mit aktivem Truck auswählen.";
        TruckPowertrainContext = string.Empty;
        TruckPowertrainDiagnostics = string.Empty;
        TruckPowertrainPairVerified = false;
        TruckPowertrainPairStatus = "Motor und Getriebe auswählen.";
        NotifyTruckPowertrainCounts();
    }

    partial void OnTruckPowertrainEngineSearchChanged(string value) => ApplyTruckPowertrainFilters();
    partial void OnTruckPowertrainTransmissionSearchChanged(string value) => ApplyTruckPowertrainFilters();
    partial void OnTruckPowertrainSelectedEngineChanged(TruckPowertrainOptionViewModel? value) => AssessTruckPowertrainPair();
    partial void OnTruckPowertrainSelectedTransmissionChanged(TruckPowertrainOptionViewModel? value) => AssessTruckPowertrainPair();

    private void ApplyTruckPowertrainFilters()
    {
        Filter(TruckPowertrainEngines, _truckEngineOptions, TruckPowertrainEngineSearch);
        Filter(TruckPowertrainTransmissions, _truckTransmissionOptions, TruckPowertrainTransmissionSearch);
        NotifyTruckPowertrainCounts();
    }

    private static void Filter(ObservableCollection<TruckPowertrainOptionViewModel> target,
        IEnumerable<TruckPowertrainOptionViewModel> source, string query)
    {
        query = query.Trim();
        var filtered = source.Where(item => query.Length == 0 || item.SearchText.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        if (target.SequenceEqual(filtered)) return;
        target.Clear();
        foreach (var item in filtered)
            target.Add(item);
    }

    private void AssessTruckPowertrainPair()
    {
        TruckPowertrainPairVerified = false;
        var engine = TruckPowertrainSelectedEngine;
        var transmission = TruckPowertrainSelectedTransmission;
        if (_truckPowertrainPreview is null || engine is null || transmission is null ||
            !_truckEngineOptions.Contains(engine) || !_truckTransmissionOptions.Contains(transmission))
        {
            TruckPowertrainPairStatus = "Motor und Getriebe aus der geprüften Vorschau auswählen.";
            return;
        }
        if (!engine.IsSelectable || !transmission.IsSelectable)
        {
            TruckPowertrainPairStatus = string.Join(Environment.NewLine, new[] { engine.ReasonText, transmission.ReasonText }.Where(text => text.Length > 0));
            return;
        }
        try
        {
            var assessment = _truckPowertrainPreview.AssessPair(engine.DataPath, transmission.DataPath);
            if (assessment.EngineDataPath != engine.DataPath || assessment.TransmissionDataPath != transmission.DataPath)
                throw new ScsSaveEditException("Die Kombinationsprüfung gehört nicht zur gewählten Auswahl.");
            TruckPowertrainPairVerified = assessment.IsVerified;
            TruckPowertrainPairStatus = assessment.IsVerified
                ? "Diese Kombination ist in der Vorschau geprüft."
                : string.Join(Environment.NewLine, assessment.Reasons);
        }
        catch (ScsSaveEditException exception) { TruckPowertrainPairStatus = exception.Message; }
    }

    private void NotifyTruckPowertrainCounts()
    {
        OnPropertyChanged(nameof(TruckPowertrainEngineCount));
        OnPropertyChanged(nameof(TruckPowertrainTransmissionCount));
    }

    private static bool SameTruckPowertrainPath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);
}

public sealed class TruckPowertrainOptionViewModel
{
    private readonly ScsTruckPowertrainOption _option;
    public TruckPowertrainOptionViewModel(ScsTruckPowertrainOption option) => _option = option with { Reasons = option.Reasons.ToArray() };
    public string Name => _option.Name;
    public string DataPath => _option.DataPath;
    public bool IsCurrent => _option.IsCurrent;
    public bool IsSelectable => _option.DefinitionVerified;
    public string SourceName => _option.SourceName;
    public string TechnicalInfo => _option.TechnicalInfo;
    public string CurrentText => IsCurrent ? "Aktuell verbaut" : string.Empty;
    public string ReasonText => string.Join(Environment.NewLine, _option.Reasons);
    public string SearchText => $"{Name} {SourceName} {TechnicalInfo} {DataPath} {ReasonText}";
}
