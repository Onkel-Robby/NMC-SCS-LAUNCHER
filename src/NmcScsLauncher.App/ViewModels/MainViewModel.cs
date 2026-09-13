using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.App.Services;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IGameInstallationDetector _gameDetector;
    private readonly ISettingsStore _settingsStore;
    private readonly IAppLogger _logger;
    private readonly IFolderPicker _folderPicker;
    private LauncherSettings _settings = new();

    [ObservableProperty]
    private string _statusText = "Lokale Konfiguration wird initialisiert …";

    [ObservableProperty]
    private string _ets2Status = "Noch nicht geprüft";

    [ObservableProperty]
    private string _ets2InstallPath = "–";

    [ObservableProperty]
    private string _atsStatus = "Noch nicht geprüft";

    [ObservableProperty]
    private string _atsInstallPath = "–";

    [ObservableProperty]
    private bool _isBusy;

    public string VersionText => "Version 0.1.0-dev";

    public MainViewModel(
        IGameInstallationDetector gameDetector,
        ISettingsStore settingsStore,
        IAppLogger logger,
        IFolderPicker folderPicker)
    {
        _gameDetector = gameDetector ?? throw new ArgumentNullException(nameof(gameDetector));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
    }

    public async Task InitializeAsync(LauncherSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        await DetectAllAsync(useSavedPaths: true, cancellationToken);
    }

    [RelayCommand]
    private Task DetectEts2Async() => DetectAndPersistAsync(GameType.Ets2, useSavedPath: false);

    [RelayCommand]
    private Task DetectAtsAsync() => DetectAndPersistAsync(GameType.Ats, useSavedPath: false);

    [RelayCommand]
    private Task BrowseEts2Async() => BrowseAndPersistAsync(GameType.Ets2);

    [RelayCommand]
    private Task BrowseAtsAsync() => BrowseAndPersistAsync(GameType.Ats);

    public void SetStartupError(string message)
    {
        StatusText = message;
    }

    private async Task DetectAllAsync(bool useSavedPaths, CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var ets2 = await _gameDetector.DetectAsync(
                GameType.Ets2,
                useSavedPaths ? _settings.Ets2InstallPath : null,
                cancellationToken);
            var ats = await _gameDetector.DetectAsync(
                GameType.Ats,
                useSavedPaths ? _settings.AtsInstallPath : null,
                cancellationToken);

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

            if (settingsChanged)
            {
                await _settingsStore.SaveAsync(_settings, cancellationToken);
            }

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
            var preferredPath = useSavedPath ? GetSavedPath(gameType) : null;
            var installation = await _gameDetector.DetectAsync(gameType, preferredPath);
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
        var selectedPath = _folderPicker.PickFolder(
            $"Installationsordner für {definition.DisplayName} auswählen",
            GetSavedPath(gameType));

        if (selectedPath is null)
        {
            return;
        }

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
        var status = installation is null
            ? "Nicht gefunden"
            : installation.Source switch
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

    private string? GetSavedPath(GameType gameType) => gameType == GameType.Ets2
        ? _settings.Ets2InstallPath
        : _settings.AtsInstallPath;

    private void SetSavedPath(GameType gameType, string path)
    {
        if (gameType == GameType.Ets2)
        {
            _settings.Ets2InstallPath = path;
        }
        else
        {
            _settings.AtsInstallPath = path;
        }
    }

    private static string BuildSummary(GameInstallation? ets2, GameInstallation? ats)
    {
        if (ets2 is not null && ats is not null)
        {
            return "ETS2 und ATS wurden erkannt. Phase 1 – Game Detection ist bereit.";
        }

        if (ets2 is not null || ats is not null)
        {
            return "Ein SCS-Spiel wurde erkannt. Fehlende Installation kann manuell ausgewählt werden.";
        }

        return "ETS2 und ATS wurden nicht automatisch gefunden. Installationsordner können manuell ausgewählt werden.";
    }
}
