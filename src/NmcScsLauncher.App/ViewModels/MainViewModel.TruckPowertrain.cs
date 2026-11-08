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
    private IScsTruckPowertrainInstaller? _truckPowertrainInstaller;
    private IScsGameProcessGuard? _truckPowertrainProcessGuard;
    private ScsTruckPowertrainInstallRequest? _truckPowertrainInstallContext;
    private CancellationTokenSource? _truckPowertrainInstallCancellation;
    private Func<bool>? _truckPowertrainInstallContextMatches;
    private TruckPowertrainOptionViewModel[] _truckEngineOptions = [];
    private TruckPowertrainOptionViewModel[] _truckTransmissionOptions = [];
    private CancellationTokenSource? _truckPowertrainCancellation;
    private Func<bool>? _truckPowertrainContextMatches;
    private long _truckPowertrainGeneration;
    private bool _truckPowertrainEventsWired;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallTruckPowertrainCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportTruckPowertrainDiagnosticsCommand))]
    private bool _truckPowertrainIsLoading;
    [ObservableProperty] private string _truckPowertrainStatus = "Spielstand mit aktivem Truck auswählen.";
    [ObservableProperty] private string _truckPowertrainContext = string.Empty;
    [ObservableProperty] private string _truckPowertrainDiagnostics = string.Empty;
    [ObservableProperty] private string _truckPowertrainEngineSearch = string.Empty;
    [ObservableProperty] private string _truckPowertrainTransmissionSearch = string.Empty;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallTruckPowertrainCommand))]
    private TruckPowertrainOptionViewModel? _truckPowertrainSelectedEngine;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallTruckPowertrainCommand))]
    private TruckPowertrainOptionViewModel? _truckPowertrainSelectedTransmission;
    [ObservableProperty] private bool _truckPowertrainPairVerified;
    [ObservableProperty] private string _truckPowertrainPairStatus = "Motor und Getriebe auswählen.";

    public ObservableCollection<TruckPowertrainOptionViewModel> TruckPowertrainEngines { get; } = new();
    public ObservableCollection<TruckPowertrainOptionViewModel> TruckPowertrainTransmissions { get; } = new();
    public string TruckPowertrainEngineCount => $"{TruckPowertrainEngines.Count} Motoren";
    public string TruckPowertrainTransmissionCount => $"{TruckPowertrainTransmissions.Count} Getriebe";

    public void ConfigureTruckPowertrainInstaller(IScsTruckPowertrainInstaller installer, IScsGameProcessGuard processGuard)
    {
        _truckPowertrainInstaller = installer ?? throw new ArgumentNullException(nameof(installer));
        _truckPowertrainProcessGuard = processGuard ?? throw new ArgumentNullException(nameof(processGuard));
        InstallTruckPowertrainCommand.NotifyCanExecuteChanged();
        NotifySaveEditorPowertrainAvailability();
    }

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

    [RelayCommand(CanExecute = nameof(PowertrainFeaturesEnabled))]
    private void RefreshTruckPowertrain()
    {
        if (!PowertrainFeaturesEnabled) return;
        ClearTruckPowertrainPreview();
        QueueTruckPowertrainInspection();
    }

    private void QueueTruckPowertrainInspection()
    {
        if (!PowertrainFeaturesEnabled) return;
        if (_truckPowertrainInstallContextMatches?.Invoke() == false)
            _truckPowertrainInstallCancellation?.Cancel();
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
        bool Current() => PowertrainFeaturesEnabled && generation == _truckPowertrainGeneration && !cancellation.IsCancellationRequested &&
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
            TruckPowertrainContext = label;
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
        if (!PowertrainFeaturesEnabled) return;
        try
        {
            var service = _truckPowertrainService!;
            var preview = await Task.Run(() => service.InspectAsync(request, cancellation.Token), cancellation.Token);
            if (!current()) return;
            var snapshot = preview.Snapshot;
            if (snapshot.Game != request.Profile.GameType ||
                !SameTruckPowertrainPath(snapshot.SaveFilePath, request.Save.GameSiiPath) ||
                snapshot.TruckId != truckId ||
                !string.Equals(snapshot.TruckModelRoot.TrimEnd('/'), modelRoot.TrimEnd('/'), StringComparison.Ordinal))
                throw new ScsSaveEditException("Die Vorschau gehört nicht mehr zum ausgewählten Truck. Bitte den Spielstand neu laden.");
            _truckPowertrainPreview = preview;
            // Keep the inspected identity and roots private; UI collections and later selections cannot replace them.
            _truckPowertrainInstallContext = new(request with
                { ModDirectories = request.ModDirectories is null ? null : Array.AsReadOnly(request.ModDirectories.ToArray()) },
                snapshot.SaveSha256, snapshot.TruckId, snapshot.CurrentEnginePath, snapshot.CurrentTransmissionPath);
            _truckEngineOptions = Options(preview.Engines, ScsPowertrainKind.Engine);
            _truckTransmissionOptions = Options(preview.Transmissions, ScsPowertrainKind.Transmission);
            ApplyTruckPowertrainFilters();
            TruckPowertrainSelectedEngine = _truckEngineOptions.FirstOrDefault(item => item.IsCurrent);
            TruckPowertrainSelectedTransmission = _truckTransmissionOptions.FirstOrDefault(item => item.IsCurrent);
            TruckPowertrainDiagnostics = string.Join(Environment.NewLine, preview.Diagnostics);
            SetTruckPowertrainDiagnostics(request, preview);
            TruckPowertrainStatus = $"Vorschau · {_truckEngineOptions.Length} Motoren und {_truckTransmissionOptions.Length} Getriebe. Vor dem Einbau werden Spielstand und Quellen erneut geprüft.";
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
        _truckPowertrainInstallContext = null;
        ClearTruckPowertrainDiagnostics();
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
        InstallTruckPowertrainCommand.NotifyCanExecuteChanged();
    }

    partial void OnTruckPowertrainEngineSearchChanged(string value) => ApplyTruckPowertrainFilters();
    partial void OnTruckPowertrainTransmissionSearchChanged(string value) => ApplyTruckPowertrainFilters();
    partial void OnTruckPowertrainSelectedEngineChanged(TruckPowertrainOptionViewModel? value) => AssessTruckPowertrainPair();
    partial void OnTruckPowertrainSelectedTransmissionChanged(TruckPowertrainOptionViewModel? value) => AssessTruckPowertrainPair();

    private void ApplyTruckPowertrainFilters()
    {
        if (!PowertrainFeaturesEnabled) return;
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
        if (!PowertrainFeaturesEnabled) return;
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

    private bool CanInstallTruckPowertrain() => TryCreateTruckPowertrainInstallRequest(out _, out _);

    private bool TryCreateTruckPowertrainInstallRequest(out ScsTruckPowertrainInstallRequest? request, out string reason)
    {
        request = null;
        reason = "Motor und Getriebe aus der aktuellen, geprüften Vorschau auswählen.";
        if (!PowertrainFeaturesEnabled) return false;
        var context = _truckPowertrainInstallContext;
        var engine = TruckPowertrainSelectedEngine;
        var transmission = TruckPowertrainSelectedTransmission;
        if (_truckPowertrainInstaller is null || _truckPowertrainProcessGuard is null || _truckPowertrainPreview is null ||
            context is null || IsBusy || SaveEditorIsInspecting || TruckPowertrainIsLoading ||
            _truckPowertrainContextMatches?.Invoke() != true || engine is null || transmission is null ||
            !_truckEngineOptions.Contains(engine) || !_truckTransmissionOptions.Contains(transmission) ||
            !engine.IsSelectable || !transmission.IsSelectable)
            return false;
        try
        {
            if (context.Content.Profile.StorageKind == ScsProfileStorageKind.SteamCloudRemote ||
                ScsProfileSaveLocator.IsSteamCloudRemoteProfileDirectory(context.Content.Profile.GameType, context.Content.Profile.ProfileDirectory))
            {
                reason = "Das Steam-Cloud-Profil ist für direkte Änderungen gesperrt.";
                return false;
            }
            if (_truckPowertrainProcessGuard.IsAnyScsGameRunning())
            {
                reason = "Für den Antriebseinbau müssen Euro Truck Simulator 2 und American Truck Simulator beendet sein.";
                return false;
            }
            if (context.ExpectedSaveSha256.Length != 64 || !context.ExpectedSaveSha256.All(Uri.IsHexDigit) ||
                string.IsNullOrWhiteSpace(context.ExpectedTruckId))
            {
                reason = "Die Identität des geprüften Spielstands ist unvollständig. Bitte erneut laden.";
                return false;
            }
            if (engine.DataPath == context.EngineDataPath && transmission.DataPath == context.TransmissionDataPath)
            {
                reason = "Diese Kombination ist bereits eingebaut.";
                return false;
            }
            var assessment = _truckPowertrainPreview.AssessPair(engine.DataPath, transmission.DataPath);
            if (!assessment.IsVerified || assessment.EngineDataPath != engine.DataPath || assessment.TransmissionDataPath != transmission.DataPath)
            {
                reason = assessment.Reasons.Count > 0 ? string.Join(Environment.NewLine, assessment.Reasons)
                    : "Die gewählte Kombination ist nicht vollständig geprüft.";
                return false;
            }
            request = context with { EngineDataPath = engine.DataPath, TransmissionDataPath = transmission.DataPath };
            return true;
        }
        catch (Exception exception)
        {
            reason = $"Die Einbauprüfung konnte nicht abgeschlossen werden: {exception.Message}";
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallTruckPowertrain))]
    private async Task InstallTruckPowertrainAsync()
    {
        if (!PowertrainFeaturesEnabled) return;
        if (!TryCreateTruckPowertrainInstallRequest(out var request, out var reason))
        {
            SaveEditorStatusText = reason;
            return;
        }
        var installer = _truckPowertrainInstaller!;
        var generation = _truckPowertrainGeneration;
        var engine = TruckPowertrainSelectedEngine!;
        var transmission = TruckPowertrainSelectedTransmission!;
        const string title = "Motor & Getriebe einbauen";
        if (!_confirmationService.ConfirmSaveEdit(title,
                $"Motor: {engine.Name}\nGetriebe: {transmission.Name}\n\nDiese Kombination in den ausgewählten Truck einbauen? " +
                "Der Spielstand und die wirksamen Quellen werden erneut geprüft. Vor dem Speichern wird ein Sicherheits-Backup erstellt. " +
                "Beide Spiele müssen beendet bleiben."))
            return;
        if (generation != _truckPowertrainGeneration || !ReferenceEquals(installer, _truckPowertrainInstaller) ||
            !ReferenceEquals(engine, TruckPowertrainSelectedEngine) || !ReferenceEquals(transmission, TruckPowertrainSelectedTransmission) ||
            _truckPowertrainContextMatches?.Invoke() != true)
        {
            SaveEditorStatusText = "Die Auswahl wurde während der Bestätigung geändert. Auswahl bitte erneut prüfen.";
            return;
        }
        if (!TryCreateTruckPowertrainInstallRequest(out var currentRequest, out reason) || currentRequest != request)
        {
            SaveEditorStatusText = reason;
            return;
        }
        var approvedRequest = request!;
        var truck = SaveEditorSelectedTruck;
        var modset = SelectedModset;
        var selectionGeneration = _saveEditorSelectionGeneration;
        var inspectionGeneration = _saveEditorInspectionGeneration;
        bool ContextMatches() => PowertrainFeaturesEnabled && approvedRequest.Content.Profile.GameType == SaveEditorSelectedGame &&
            ReferenceEquals(approvedRequest.Content.Profile, SaveEditorSelectedProfile) &&
            ReferenceEquals(approvedRequest.Content.Save, SaveEditorSelectedSave) &&
            ReferenceEquals(truck, SaveEditorSelectedTruck) && ReferenceEquals(modset, SelectedModset) &&
            selectionGeneration == _saveEditorSelectionGeneration && inspectionGeneration == _saveEditorInspectionGeneration &&
            string.Equals(approvedRequest.Content.PreferredInstallPath, GetSavedPath(approvedRequest.Content.Profile.GameType), StringComparison.OrdinalIgnoreCase);
        using var cancellation = new CancellationTokenSource();
        _truckPowertrainInstallCancellation = cancellation;
        _truckPowertrainInstallContextMatches = ContextMatches;
        try
        {
            // IsBusy clears the preview, so the installation has its own context and cancellation lifetime.
            await ExecuteSaveEditorEditAsync(title, async () =>
            {
                try { return await installer.InstallAsync(approvedRequest, cancellation.Token); }
                finally
                {
                    _truckPowertrainInstallCancellation = null;
                    _truckPowertrainInstallContextMatches = null;
                }
            }, selectionIsCurrent: ContextMatches);
        }
        finally
        {
            _truckPowertrainInstallCancellation = null;
            _truckPowertrainInstallContextMatches = null;
        }
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
    public TruckPowertrainOptionViewModel(ScsTruckPowertrainOption option) => _option = option with
        { Reasons = option.Reasons.ToArray(), DiagnosticDetails = option.DiagnosticDetails?.ToArray() };
    public string Name => _option.Name;
    public string DataPath => _option.DataPath;
    public bool IsCurrent => _option.IsCurrent;
    public bool IsSelectable => _option.DefinitionVerified;
    public string SourceName => _option.SourceName;
    public string TechnicalInfo => _option.TechnicalInfo;
    public string CurrentText => IsCurrent ? "Aktuell verbaut" : string.Empty;
    public string ReasonText => string.Join(Environment.NewLine, _option.Reasons);
    public string DiagnosticText => string.Join(Environment.NewLine + Environment.NewLine,
        _option.DiagnosticDetails?.Take(16).Select(issue => new TruckPowertrainDiagnosticViewModel(issue).TechnicalText) ?? []);
    public string SearchText => $"{Name} {SourceName} {TechnicalInfo} {DataPath} {ReasonText}";
}
