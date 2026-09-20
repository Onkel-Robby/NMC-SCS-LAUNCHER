using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.App.Services;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IGameInstallationDetector _gameDetector;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IModsetInspector _modsetInspector;
    private readonly ISettingsStore _settingsStore;
    private readonly IAppLogger _logger;
    private readonly IFolderPicker _folderPicker;
    private readonly IModsetManager _modsetManager;
    private readonly IModsetEditorService _modsetEditor;
    private readonly IConfirmationService _confirmationService;
    private readonly IExplorerService _explorerService;
    private readonly IStartCheckDialogService _startCheckDialogService;
    private LauncherSettings _settings = new();
    private ModsetInspection? _selectedInspection;
    private bool _initializingSelection;

    [ObservableProperty] private int _currentPageIndex;
    [ObservableProperty] private string _statusText = "Lokale Konfiguration wird initialisiert …";
    [ObservableProperty] private string _ets2Status = "Noch nicht geprüft";
    [ObservableProperty] private string _ets2InstallPath = "–";
    [ObservableProperty] private string _atsStatus = "Noch nicht geprüft";
    [ObservableProperty] private string _atsInstallPath = "–";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private Modset? _selectedModset;
    [ObservableProperty] private string _selectedModCountText = "–";
    [ObservableProperty] private string _selectedProfileCountText = "–";
    [ObservableProperty] private string _selectedInspectionStatus = "Modset auswählen, um Mods und Profile zu prüfen.";
    [ObservableProperty] private string _selectedModDirectory = "–";
    [ObservableProperty] private string _selectedLocalProfilesDirectory = "–";
    [ObservableProperty] private string _selectedSteamProfilesDirectory = "–";
    [ObservableProperty] private string _settingsDefaultModsetRoot = string.Empty;
    [ObservableProperty] private bool _settingsShowStartCheck = true;
    [ObservableProperty] private bool _settingsShowModCount = true;
    [ObservableProperty] private bool _settingsRememberLastModset = true;

    public ObservableCollection<Modset> Modsets { get; } = new();
    public ObservableCollection<Modset> Ets2Modsets { get; } = new();
    public ObservableCollection<Modset> AtsModsets { get; } = new();
    public ObservableCollection<Modset> DashboardModsets { get; } = new();

    public string ModsetCountText => Modsets.Count == 1 ? "1 Modset" : $"{Modsets.Count} Modsets";
    public string Ets2ModsetCountText => Ets2Modsets.Count == 1 ? "1 Modset" : $"{Ets2Modsets.Count} Modsets";
    public string AtsModsetCountText => AtsModsets.Count == 1 ? "1 Modset" : $"{AtsModsets.Count} Modsets";
    public string SelectedModsetName => SelectedModset?.Name ?? "Kein Modset ausgewählt";
    public string SelectedModsetGameText => SelectedModset is null ? "–" : GameDefinition.For(SelectedModset.Game).DisplayName;
    public string VersionText => AppVersionInfo.Display;

    public MainViewModel(
        IGameInstallationDetector gameDetector,
        IGameLaunchService gameLaunchService,
        IModsetInspector modsetInspector,
        ISettingsStore settingsStore,
        IAppLogger logger,
        IFolderPicker folderPicker,
        IModsetManager modsetManager,
        IModsetEditorService modsetEditor,
        IConfirmationService confirmationService,
        IExplorerService explorerService,
        IStartCheckDialogService startCheckDialogService)
    {
        _gameDetector = gameDetector ?? throw new ArgumentNullException(nameof(gameDetector));
        _gameLaunchService = gameLaunchService ?? throw new ArgumentNullException(nameof(gameLaunchService));
        _modsetInspector = modsetInspector ?? throw new ArgumentNullException(nameof(modsetInspector));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _modsetManager = modsetManager ?? throw new ArgumentNullException(nameof(modsetManager));
        _modsetEditor = modsetEditor ?? throw new ArgumentNullException(nameof(modsetEditor));
        _confirmationService = confirmationService ?? throw new ArgumentNullException(nameof(confirmationService));
        _explorerService = explorerService ?? throw new ArgumentNullException(nameof(explorerService));
        _startCheckDialogService = startCheckDialogService ?? throw new ArgumentNullException(nameof(startCheckDialogService));
    }

    public async Task InitializeAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        ApplySettingsToUi();
        await DetectAllAsync(useSavedPaths: true, cancellationToken);
        await LoadModsetsAsync(cancellationToken);

        _initializingSelection = true;
        try
        {
            if (_settings.RememberLastModset && _settings.LastSelectedModsetId is Guid lastId)
            {
                SelectedModset = Modsets.FirstOrDefault(item => item.Id == lastId);
            }
        }
        finally
        {
            _initializingSelection = false;
        }
    }

    [RelayCommand] private Task DetectEts2Async() => DetectAndPersistAsync(GameType.Ets2, useSavedPath: false);
    [RelayCommand] private Task DetectAtsAsync() => DetectAndPersistAsync(GameType.Ats, useSavedPath: false);
    [RelayCommand] private Task BrowseEts2Async() => BrowseAndPersistAsync(GameType.Ets2);
    [RelayCommand] private Task BrowseAtsAsync() => BrowseAndPersistAsync(GameType.Ats);

    [RelayCommand]
    private void BrowseDefaultModsetRoot()
    {
        var selected = _folderPicker.PickFolder("Standardordner für neue Modsets auswählen", SettingsDefaultModsetRoot);
        if (selected is not null)
        {
            SettingsDefaultModsetRoot = selected;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        _settings.DefaultModsetRoot = string.IsNullOrWhiteSpace(SettingsDefaultModsetRoot) ? null : SettingsDefaultModsetRoot.Trim();
        _settings.ShowStartCheck = SettingsShowStartCheck;
        _settings.ShowModCount = SettingsShowModCount;
        _settings.RememberLastModset = SettingsRememberLastModset;
        _settings.LastSelectedModsetId = SettingsRememberLastModset ? SelectedModset?.Id : null;

        try
        {
            await _settingsStore.SaveAsync(_settings);
            await InspectSelectedModsetAsync(SelectedModset);
            StatusText = "Einstellungen wurden gespeichert.";
        }
        catch (Exception ex)
        {
            StatusText = "Einstellungen konnten nicht gespeichert werden. Details wurden protokolliert.";
            await _logger.WriteAsync("ERROR", "Saving launcher settings failed.", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedModset))]
    private Task RefreshSelectedInspectionAsync() => InspectSelectedModsetAsync(SelectedModset);

    [RelayCommand(CanExecute = nameof(HasSelectedModset))]
    private Task OpenModDirectoryAsync() => OpenInspectionDirectoryAsync(_selectedInspection?.ModDirectory, "Mod-Ordner");

    [RelayCommand(CanExecute = nameof(HasSelectedModset))]
    private Task OpenLocalProfilesDirectoryAsync() => OpenInspectionDirectoryAsync(_selectedInspection?.LocalProfilesDirectory, "Ordner der lokalen Profile");

    [RelayCommand(CanExecute = nameof(HasSelectedModset))]
    private Task OpenSteamProfilesDirectoryAsync() => OpenInspectionDirectoryAsync(_selectedInspection?.SteamProfilesDirectory, "Ordner der Steam-Profile");

    [RelayCommand(CanExecute = nameof(HasSelectedModset))]
    private async Task LaunchSelectedModsetAsync()
    {
        var selected = SelectedModset;
        if (selected is null) return;
        IsBusy = true;
        try
        {
            var installation = await _gameDetector.DetectAsync(selected.Game, GetSavedPath(selected.Game));
            if (installation is null)
            {
                StatusText = $"{GameDefinition.For(selected.Game).DisplayName} wurde nicht gefunden. Prüfe zuerst den Installationspfad.";
                return;
            }

            if (!string.Equals(GetSavedPath(selected.Game), installation.InstallPath, StringComparison.OrdinalIgnoreCase))
            {
                SetSavedPath(selected.Game, installation.InstallPath);
                await _settingsStore.SaveAsync(_settings);
                ApplyInstallation(selected.Game, installation);
            }

            var plan = await _gameLaunchService.PrepareAsync(selected, installation);
            if (!plan.CanLaunch)
            {
                _startCheckDialogService.ConfirmLaunch(plan);
                StatusText = "Die Startprüfung enthält kritische Fehler. Das Spiel wurde nicht gestartet.";
                return;
            }

            if (_settings.ShowStartCheck && !_startCheckDialogService.ConfirmLaunch(plan))
            {
                StatusText = "Spielstart wurde abgebrochen.";
                return;
            }

            var processId = await _gameLaunchService.LaunchAsync(plan);
            await _modsetManager.MarkStartedAsync(selected.Id, DateTimeOffset.UtcNow);
            await _logger.WriteAsync("INFO", $"Started {selected.Game} with modset {selected.Id}. ProcessId={processId}; ModDirectory={ScsModsetPathResolver.GetModDirectory(selected)}");
            await LoadModsetsAsync();
            SelectedModset = Modsets.FirstOrDefault(item => item.Id == selected.Id);
            StatusText = $"{GameDefinition.For(selected.Game).DisplayName} wurde mit '{selected.Name}' gestartet (PID {processId}).";
        }
        catch (Exception ex)
        {
            StatusText = "Das Spiel konnte nicht gestartet werden. Details wurden protokolliert.";
            await _logger.WriteAsync("ERROR", $"Game launch failed for modset {selected.Id}.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateModsetAsync()
    {
        var result = _modsetEditor.Show(ModsetEditorMode.Create, new ModsetEditorData(
            GameType.Ets2,
            "Neues Modset",
            null,
            BuildSuggestedModPath(GameType.Ets2, "Neues Modset"),
            null,
            null));
        if (result is null) return;
        await ExecuteModsetChangeAsync(() => _modsetManager.CreateAsync(ToDraft(result)), "Modset wurde erstellt.");
    }

    [RelayCommand]
    private async Task ImportModsetAsync()
    {
        var result = _modsetEditor.Show(ModsetEditorMode.Import, new ModsetEditorData(
            GameType.Ets2,
            "Importiertes Modset",
            null,
            string.Empty,
            null,
            null));
        if (result is null) return;
        await ExecuteModsetChangeAsync(() => _modsetManager.ImportAsync(ToDraft(result)), "Bestehendes Modset wurde importiert.");
    }

    [RelayCommand(CanExecute = nameof(HasSelectedModset))]
    private async Task EditModsetAsync()
    {
        var selected = SelectedModset;
        if (selected is null) return;
        var result = _modsetEditor.Show(ModsetEditorMode.Edit, new ModsetEditorData(
            selected.Game,
            selected.Name,
            selected.Description,
            ScsModsetPathResolver.GetModDirectory(selected),
            selected.PreferredProfile,
            selected.AdditionalLaunchArguments));
        if (result is null) return;
        await ExecuteModsetChangeAsync(() => _modsetManager.UpdateAsync(selected.Id, ToDraft(result)), "Modset wurde aktualisiert.");
    }

    [RelayCommand(CanExecute = nameof(HasSelectedModset))]
    private async Task RemoveModsetAsync()
    {
        var selected = SelectedModset;
        if (selected is null || !_confirmationService.ConfirmRemoveFromLauncher(selected)) return;
        try
        {
            await _modsetManager.RemoveAsync(selected.Id);
            await _logger.WriteAsync("INFO", $"Removed modset {selected.Id} from launcher without deleting files.");
            SelectedModset = null;
            await LoadModsetsAsync();
            StatusText = "Modset wurde aus dem Launcher entfernt. Dateien wurden nicht gelöscht.";
        }
        catch (Exception ex)
        {
            await HandleModsetErrorAsync("Das Modset konnte nicht entfernt werden.", ex);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedModset))]
    private async Task OpenModsetHomeAsync()
    {
        var selected = SelectedModset;
        if (selected is null) return;
        try
        {
            _explorerService.OpenFolder(ScsModsetPathResolver.GetModDirectory(selected));
        }
        catch (Exception ex)
        {
            await HandleModsetErrorAsync("Der Mod-Ordner konnte nicht geöffnet werden.", ex);
        }
    }

    partial void OnSelectedModsetChanged(Modset? value)
    {
        LaunchSelectedModsetCommand.NotifyCanExecuteChanged();
        EditModsetCommand.NotifyCanExecuteChanged();
        RemoveModsetCommand.NotifyCanExecuteChanged();
        OpenModsetHomeCommand.NotifyCanExecuteChanged();
        RefreshSelectedInspectionCommand.NotifyCanExecuteChanged();
        OpenModDirectoryCommand.NotifyCanExecuteChanged();
        OpenLocalProfilesDirectoryCommand.NotifyCanExecuteChanged();
        OpenSteamProfilesDirectoryCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SelectedModsetName));
        OnPropertyChanged(nameof(SelectedModsetGameText));
        _ = InspectSelectedModsetAsync(value);

        if (!_initializingSelection && _settings.RememberLastModset)
        {
            _ = PersistLastSelectedModsetAsync(value?.Id);
        }
    }

    public void SetStartupError(string message) => StatusText = message;

    private bool HasSelectedModset() => SelectedModset is not null;

    private void ApplySettingsToUi()
    {
        SettingsDefaultModsetRoot = _settings.DefaultModsetRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NMC SCS Launcher",
            "Mods");
        SettingsShowStartCheck = _settings.ShowStartCheck;
        SettingsShowModCount = _settings.ShowModCount;
        SettingsRememberLastModset = _settings.RememberLastModset;
    }

    private async Task PersistLastSelectedModsetAsync(Guid? id)
    {
        try
        {
            _settings.LastSelectedModsetId = id;
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception ex)
        {
            await _logger.WriteAsync("ERROR", "Persisting last selected modset failed.", ex);
        }
    }

    private async Task InspectSelectedModsetAsync(Modset? modset)
    {
        if (modset is null)
        {
            ClearInspection();
            return;
        }

        try
        {
            var inspection = await _modsetInspector.InspectAsync(modset);
            if (SelectedModset?.Id != modset.Id) return;

            _selectedInspection = inspection;
            SelectedModCountText = _settings.ShowModCount
                ? $"{inspection.TotalModCount} ({inspection.PackageModCount} .scs + {inspection.ExtractedModCount} Ordner)"
                : "In Einstellungen ausgeblendet";
            SelectedProfileCountText = $"{inspection.TotalProfileCount} ({inspection.LocalProfileCount} lokal + {inspection.SteamProfileCount} Steam)";
            SelectedModDirectory = inspection.ModDirectory;
            SelectedLocalProfilesDirectory = inspection.LocalProfilesDirectory;
            SelectedSteamProfilesDirectory = inspection.SteamProfilesDirectory;
            SelectedInspectionStatus = inspection.GameDataDirectoryExists
                ? inspection.Warnings.Count == 0 ? "SCS-Datenordner erfolgreich gelesen." : string.Join(" ", inspection.Warnings)
                : inspection.Warnings.FirstOrDefault() ?? "SCS-Datenordner ist noch nicht vorhanden.";
        }
        catch (Exception ex)
        {
            if (SelectedModset?.Id != modset.Id) return;
            ClearInspection();
            SelectedInspectionStatus = "Mods und Profile konnten nicht gelesen werden.";
            await _logger.WriteAsync("ERROR", $"Modset inspection failed for {modset.Id}.", ex);
        }
    }

    private async Task OpenInspectionDirectoryAsync(string? path, string description)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            _explorerService.OpenFolder(path);
            StatusText = $"{description} geöffnet.";
        }
        catch (DirectoryNotFoundException)
        {
            StatusText = $"{description} ist noch nicht vorhanden.";
        }
        catch (Exception ex)
        {
            await HandleModsetErrorAsync($"{description} konnte nicht geöffnet werden.", ex);
        }
    }

    private void ClearInspection()
    {
        _selectedInspection = null;
        SelectedModCountText = "–";
        SelectedProfileCountText = "–";
        SelectedModDirectory = "–";
        SelectedLocalProfilesDirectory = "–";
        SelectedSteamProfilesDirectory = "–";
        SelectedInspectionStatus = "Modset auswählen, um Mods und Profile zu prüfen.";
    }

    private async Task ExecuteModsetChangeAsync(Func<Task<Modset>> operation, string successMessage)
    {
        try
        {
            var modset = await operation();
            await _logger.WriteAsync("INFO", $"Modset change persisted: {modset.Id} / {modset.Name}");
            await LoadModsetsAsync();
            SelectedModset = Modsets.FirstOrDefault(item => item.Id == modset.Id);
            StatusText = successMessage;
        }
        catch (ModsetValidationException ex)
        {
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            await HandleModsetErrorAsync("Die Modset-Änderung konnte nicht gespeichert werden.", ex);
        }
    }

    private async Task HandleModsetErrorAsync(string userMessage, Exception exception)
    {
        StatusText = userMessage + " Details wurden protokolliert.";
        await _logger.WriteAsync("ERROR", userMessage, exception);
    }

    private async Task LoadModsetsAsync(CancellationToken cancellationToken = default)
    {
        var selectedId = SelectedModset?.Id;
        var modsets = await _modsetManager.GetAllAsync(cancellationToken);

        Modsets.Clear();
        Ets2Modsets.Clear();
        AtsModsets.Clear();
        DashboardModsets.Clear();

        foreach (var modset in modsets)
        {
            Modsets.Add(modset);
            if (modset.Game == GameType.Ets2) Ets2Modsets.Add(modset);
            else AtsModsets.Add(modset);
        }

        foreach (var modset in modsets
                     .OrderByDescending(static item => item.LastStartedAt ?? DateTimeOffset.MinValue)
                     .ThenBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                     .Take(4))
        {
            DashboardModsets.Add(modset);
        }

        SelectedModset = selectedId is null ? null : Modsets.FirstOrDefault(item => item.Id == selectedId);
        OnPropertyChanged(nameof(ModsetCountText));
        OnPropertyChanged(nameof(Ets2ModsetCountText));
        OnPropertyChanged(nameof(AtsModsetCountText));
    }

    private string BuildSuggestedModPath(GameType game, string name)
    {
        var root = _settings.DefaultModsetRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NMC SCS Launcher",
                "Mods");
        }

        return Path.Combine(root, game == GameType.Ets2 ? "ETS2" : "ATS", name);
    }

    private static ModsetDraft ToDraft(ModsetEditorData data) => new(
        data.Game,
        data.Name,
        data.Description,
        string.Empty,
        data.PreferredProfile,
        data.AdditionalLaunchArguments,
        data.ModDirectoryPath);

    private async Task DetectAllAsync(bool useSavedPaths, CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var ets2 = await _gameDetector.DetectAsync(GameType.Ets2, useSavedPaths ? _settings.Ets2InstallPath : null, cancellationToken);
            var ats = await _gameDetector.DetectAsync(GameType.Ats, useSavedPaths ? _settings.AtsInstallPath : null, cancellationToken);
            ApplyInstallation(GameType.Ets2, ets2);
            ApplyInstallation(GameType.Ats, ats);

            var settingsChanged = false;
            if (ets2 is not null && !string.Equals(_settings.Ets2InstallPath, ets2.InstallPath, StringComparison.OrdinalIgnoreCase))
            {
                _settings.Ets2InstallPath = ets2.InstallPath;
                settingsChanged = true;
            }

            if (ats is not null && !string.Equals(_settings.AtsInstallPath, ats.InstallPath, StringComparison.OrdinalIgnoreCase))
            {
                _settings.AtsInstallPath = ats.InstallPath;
                settingsChanged = true;
            }

            if (settingsChanged) await _settingsStore.SaveAsync(_settings, cancellationToken);
            StatusText = BuildSummary(ets2, ats);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DetectAndPersistAsync(GameType gameType, bool useSavedPath)
    {
        IsBusy = true;
        try
        {
            var installation = await _gameDetector.DetectAsync(gameType, useSavedPath ? GetSavedPath(gameType) : null);
            ApplyInstallation(gameType, installation);
            if (installation is null)
            {
                StatusText = $"{GameDefinition.For(gameType).DisplayName} wurde über Steam nicht gefunden.";
                return;
            }

            SetSavedPath(gameType, installation.InstallPath);
            await _settingsStore.SaveAsync(_settings);
            await _logger.WriteAsync("INFO", $"Detected {gameType} at {installation.InstallPath}");
            StatusText = $"{GameDefinition.For(gameType).DisplayName} wurde erkannt und gespeichert.";
        }
        catch (Exception ex)
        {
            StatusText = "Die Spielerkennung ist fehlgeschlagen. Details wurden protokolliert.";
            await _logger.WriteAsync("ERROR", $"Game detection failed for {gameType}.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BrowseAndPersistAsync(GameType gameType)
    {
        var definition = GameDefinition.For(gameType);
        var selectedPath = _folderPicker.PickFolder($"Installationsordner für {definition.DisplayName} auswählen", GetSavedPath(gameType));
        if (selectedPath is null) return;

        var installation = _gameDetector.Validate(gameType, selectedPath, GameInstallationSource.ManualSelection);
        ApplyInstallation(gameType, installation);
        if (installation is null)
        {
            StatusText = $"Ungültiger Installationsordner: {definition.ExecutableRelativePath} wurde nicht gefunden.";
            return;
        }

        SetSavedPath(gameType, installation.InstallPath);
        await _settingsStore.SaveAsync(_settings);
        await _logger.WriteAsync("INFO", $"Manually selected {gameType} at {installation.InstallPath}");
        StatusText = $"{definition.DisplayName}: manueller Installationspfad gespeichert.";
    }

    private void ApplyInstallation(GameType gameType, GameInstallation? installation)
    {
        var status = installation is null ? "Nicht gefunden" : installation.Source switch
        {
            GameInstallationSource.SavedPath => "Gefunden · gespeicherter Pfad",
            GameInstallationSource.SteamAutoDetection => "Gefunden · Steam",
            GameInstallationSource.ManualSelection => "Gefunden · manuell",
            _ => "Gefunden"
        };
        var path = installation?.InstallPath ?? "–";

        if (gameType == GameType.Ets2)
        {
            Ets2Status = status;
            Ets2InstallPath = path;
        }
        else
        {
            AtsStatus = status;
            AtsInstallPath = path;
        }
    }

    private string? GetSavedPath(GameType gameType) => gameType == GameType.Ets2 ? _settings.Ets2InstallPath : _settings.AtsInstallPath;

    private void SetSavedPath(GameType gameType, string path)
    {
        if (gameType == GameType.Ets2) _settings.Ets2InstallPath = path;
        else _settings.AtsInstallPath = path;
    }

    private static string BuildSummary(GameInstallation? ets2, GameInstallation? ats)
    {
        if (ets2 is not null && ats is not null) return "ETS2 und ATS wurden erkannt. Modsets können gestartet werden.";
        if (ets2 is not null || ats is not null) return "Ein SCS-Spiel wurde erkannt. Fehlende Installation kann manuell ausgewählt werden.";
        return "ETS2 und ATS wurden nicht automatisch gefunden. Installationsordner können manuell ausgewählt werden.";
    }
}
