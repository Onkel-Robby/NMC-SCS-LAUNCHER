using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.App.Services;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;

namespace NmcScsLauncher.App.ViewModels;

public partial class MainViewModel
{
    private IScsProfileSaveLocator? _saveEditorLocator;
    private IScsProfileEditService? _saveEditorProfileEditService;
    private IScsVehicleEditService? _saveEditorVehicleEditService;
    private IScsSaveEditService? _saveEditorSaveEditService;
    private string? _saveEditorLastEditedFilePath;
    private ScsSaveKind? _saveEditorLastEditedSaveKind;
    private DateTimeOffset? _saveEditorLastEditedLastWriteTimeUtc;
    // A reload performs an awaited inspection and must not also start one via the
    // SelectedSave property callback; otherwise the status updates can race.
    private bool _suppressSaveEditorAutomaticInspection;
    private long _saveEditorProfileLoadGeneration;
    private long _saveEditorInspectionGeneration;
    private long _saveEditorDiscoveryGeneration;
    private long _saveEditorSelectionGeneration;
    private string? _saveEditorActiveTruckId;
    private string? _saveEditorActiveTrailerId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveEditorCanEditPlates))]
    [NotifyCanExecuteChangedFor(nameof(SetSaveEditorTruckPlateCommand), nameof(SetSaveEditorTrailerPlateCommand))]
    private bool _saveEditorIsInspecting;

    public bool SaveEditorCanEditPlates => SaveEditorSelectedSave is not null &&
        _saveEditorVehicleEditService is not null && !SaveEditorIsInspecting && !IsBusy;

    [ObservableProperty] private GameType _saveEditorSelectedGame = GameType.Ets2;
    [ObservableProperty] private ScsProfileReference? _saveEditorSelectedProfile;
    [ObservableProperty] private ScsSaveReference? _saveEditorSelectedSave;
    [ObservableProperty] private ScsVehicleInventoryItem? _saveEditorSelectedTruck;
    [ObservableProperty] private ScsVehicleInventoryItem? _saveEditorSelectedTrailer;
    [ObservableProperty] private string _saveEditorStatusText = "Spiel auswählen und Profile laden.";
    [ObservableProperty] private string _saveEditorGameHome = "–";
    [ObservableProperty] private string _saveEditorCloudNotice = string.Empty;
    [ObservableProperty] private string _saveEditorProfileNameText = string.Empty;
    [ObservableProperty] private string _saveEditorMoneyText = string.Empty;
    [ObservableProperty] private string _saveEditorExperienceText = string.Empty;
    [ObservableProperty] private string _saveEditorAdrMaskText = string.Empty;
    [ObservableProperty] private string _saveEditorLongDistanceText = string.Empty;
    [ObservableProperty] private string _saveEditorHighValueCargoText = string.Empty;
    [ObservableProperty] private string _saveEditorFragileCargoText = string.Empty;
    [ObservableProperty] private string _saveEditorUrgentDeliveryText = string.Empty;
    [ObservableProperty] private string _saveEditorEcoDrivingText = string.Empty;
    [ObservableProperty] private string _saveEditorFuelText = string.Empty;
    [ObservableProperty] private string _saveEditorMileageText = string.Empty;
    [ObservableProperty] private string _saveEditorCargoMassText = string.Empty;
    [ObservableProperty] private string _saveEditorEnginePath = string.Empty;
    [ObservableProperty] private string _saveEditorTransmissionPath = string.Empty;
    [ObservableProperty] private ScsPowertrainCatalogItem? _saveEditorSelectedEngine;
    [ObservableProperty] private ScsPowertrainCatalogItem? _saveEditorSelectedTransmission;
    [ObservableProperty] private string _saveEditorActiveTruckText = "–";
    [ObservableProperty] private string _saveEditorActiveTrailerText = "–";
    [ObservableProperty] private string _saveEditorLastBackupPath = "–";

    public IReadOnlyList<GameType> SaveEditorGames { get; } = Enum.GetValues<GameType>();
    public LicensePlateEditorViewModel SaveEditorTruckPlate { get; } = new(GameType.Ets2);
    public LicensePlateEditorViewModel SaveEditorTrailerPlate { get; } = new(GameType.Ets2);
    public ObservableCollection<ScsProfileReference> SaveEditorProfiles { get; } = new();
    public ObservableCollection<ScsSaveReference> SaveEditorSaves { get; } = new();
    public ObservableCollection<ScsVehicleInventoryItem> SaveEditorTrucks { get; } = new();
    public ObservableCollection<ScsVehicleInventoryItem> SaveEditorTrailers { get; } = new();
    public ObservableCollection<ScsPowertrainCatalogItem> SaveEditorEngines { get; } = new();
    public ObservableCollection<ScsPowertrainCatalogItem> SaveEditorTransmissions { get; } = new();

    public void ConfigureSaveEditorServices(
        IScsProfileSaveLocator locator,
        IScsProfileEditService profileEditService,
        IScsVehicleEditService vehicleEditService,
        IScsSaveEditService saveEditService)
    {
        _saveEditorLocator = locator ?? throw new ArgumentNullException(nameof(locator));
        _saveEditorProfileEditService = profileEditService ?? throw new ArgumentNullException(nameof(profileEditService));
        _saveEditorVehicleEditService = vehicleEditService ?? throw new ArgumentNullException(nameof(vehicleEditService));
        _saveEditorSaveEditService = saveEditService ?? throw new ArgumentNullException(nameof(saveEditService));
    }

    [RelayCommand]
    private async Task RefreshSaveEditorAsync()
    {
        if (_saveEditorLocator is null)
        {
            SaveEditorStatusText = "Save-Editor-Dienste sind nicht initialisiert.";
            return;
        }

        var selectedGame = SaveEditorSelectedGame;
        var generation = ++_saveEditorDiscoveryGeneration;
        bool IsCurrent() => generation == _saveEditorDiscoveryGeneration && selectedGame == SaveEditorSelectedGame;
        IsBusy = true;
        try
        {
            var candidates = ScsProfileHomeLocator.GetCandidates(
                selectedGame,
                GetConfiguredSaveEditorHome(),
                ScsProfileHomeLocator.GetDocumentRoots());
            var home = candidates.FirstOrDefault() ?? GetDefaultSaveEditorHome();
            IReadOnlyList<ScsProfileReference> profiles = Array.Empty<ScsProfileReference>();
            var attemptedPaths = new List<string>();

            foreach (var candidate in candidates)
            {
                if (!Directory.Exists(candidate))
                    continue;

                attemptedPaths.Add(candidate);
                var discovered = await _saveEditorLocator.FindProfilesAsync(
                    selectedGame,
                    candidate);
                if (!IsCurrent()) return;

                if (discovered.Count == 0)
                    continue;

                home = candidate;
                profiles = discovered;
                break;
            }

            // Local steam_profiles usually holds machine-specific settings,
            // while actual Steam Cloud game.sii saves live in Steam userdata.
            var remoteProfiles = await _saveEditorLocator.FindSteamCloudProfilesAsync(selectedGame);
            if (!IsCurrent()) return;
            var combined = remoteProfiles.Concat(profiles)
                .DistinctBy(static item => item.ProfileDirectory, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var previousProfilePath = SaveEditorSelectedProfile?.ProfileDirectory;
            SaveEditorProfiles.Clear();
            foreach (var profile in combined)
                SaveEditorProfiles.Add(profile);

            SaveEditorSelectedProfile =
                combined.FirstOrDefault(item =>
                    string.Equals(item.ProfileDirectory, previousProfilePath, StringComparison.OrdinalIgnoreCase))
                ?? combined.FirstOrDefault();
            SaveEditorGameHome = SaveEditorSelectedProfile?.GameHomeDirectory ?? home;

            if (SaveEditorSelectedProfile is null)
            {
                ClearSaveEditorSaveState();
                SaveEditorStatusText =
                    $"{GameDefinition.For(SaveEditorSelectedGame).DisplayName}: keine Profile mit profile.sii gefunden. " +
                    "Dokumenten- und Steam-Cloud-Verzeichnisse geprüft. Bitte Profilpfade kontrollieren.";
                await _logger.WriteAsync("WARN",
                    $"Save editor found no profiles for {SaveEditorSelectedGame}. " +
                    $"Searched document directories: {string.Join("; ", attemptedPaths)}; " +
                    $"First candidate: {home}; Remote Steam profiles: {remoteProfiles.Count}");
            }
            else
            {
                SaveEditorStatusText =
                    $"{combined.Length} Profil(e) gefunden (davon {remoteProfiles.Count} Steam Cloud). " +
                    "Profil und Save auswählen.";
                await _logger.WriteAsync("INFO",
                    $"Save editor profiles found. Game={SaveEditorSelectedGame}; DocumentHome={home}; " +
                    $"DocumentCount={profiles.Count}; RemoteCloudCount={remoteProfiles.Count}");
            }
        }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            SaveEditorProfiles.Clear();
            ClearSaveEditorSaveState();
            SaveEditorStatusText = "Profile konnten nicht gelesen werden. Details wurden protokolliert.";
            await _logger.WriteAsync("ERROR", "Save editor profile discovery failed.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ChooseSaveEditorProfileHomeAsync()
    {
        if (_saveEditorLocator is null)
            return;

        var selectedGame = SaveEditorSelectedGame;
        var discoveryGeneration = _saveEditorDiscoveryGeneration;
        var selected = _folderPicker.PickFolder(
            $"Profilverzeichnis für {GameDefinition.For(selectedGame).DisplayName} wählen",
            GetConfiguredSaveEditorHome() ?? GetDefaultSaveEditorHome());
        if (selected is null)
            return;

        string home;
        try
        {
            home = ScsProfileHomeLocator.NormalizeSelectedHome(selected);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            SaveEditorStatusText = "Der ausgewählte Profilordner ist ungültig.";
            return;
        }

        if (!Directory.Exists(home))
        {
            SaveEditorStatusText = "Der ausgewählte Profilordner existiert nicht.";
            return;
        }

        try
        {
            var profiles = await _saveEditorLocator.FindProfilesAsync(selectedGame, home);
            if (selectedGame != SaveEditorSelectedGame || discoveryGeneration != _saveEditorDiscoveryGeneration)
                return;
            if (profiles.Count == 0)
            {
                SaveEditorStatusText =
                    "In diesem Verzeichnis wurden keine Profile gefunden. " +
                    "Bitte den Ordner „Euro Truck Simulator 2“ bzw. „American Truck Simulator“ auswählen, " +
                    "der „profiles“ oder „steam_profiles“ enthält.";
                return;
            }

            if (selectedGame == GameType.Ets2)
                _settings.Ets2ProfileHomePath = home;
            else
                _settings.AtsProfileHomePath = home;

            await _settingsStore.SaveAsync(_settings);
            await _logger.WriteAsync("INFO", $"Save editor profile home selected: {selectedGame}; Home={home}");
            await RefreshSaveEditorAsync();
        }
        catch (Exception ex)
        {
            if (selectedGame != SaveEditorSelectedGame || discoveryGeneration != _saveEditorDiscoveryGeneration)
                return;
            SaveEditorStatusText = "Der Profilordner konnte nicht übernommen werden. Details wurden protokolliert.";
            await _logger.WriteAsync("ERROR", "Save editor profile home selection failed.", ex);
        }
    }

    private string? GetConfiguredSaveEditorHome() =>
        SaveEditorSelectedGame == GameType.Ets2
            ? _settings.Ets2ProfileHomePath
            : _settings.AtsProfileHomePath;

    private string GetDefaultSaveEditorHome() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            GameDefinition.For(SaveEditorSelectedGame).HomeDirectoryName);

    partial void OnSaveEditorSelectedGameChanged(GameType value)
    {
        ++_saveEditorDiscoveryGeneration;
        ++_saveEditorProfileLoadGeneration;
        SaveEditorProfiles.Clear();
        SaveEditorSelectedProfile = null;
        ClearSaveEditorSaveState();
        SaveEditorGameHome = GetConfiguredSaveEditorHome() ?? GetDefaultSaveEditorHome();
        SaveEditorStatusText =
            $"{GameDefinition.For(value).DisplayName}: auf „Profile laden“ klicken.";
    }

    partial void OnSaveEditorSelectedProfileChanged(ScsProfileReference? value)
    {
        SaveEditorProfileNameText = value?.DisplayName ?? string.Empty;
        SaveEditorCloudNotice = value?.StorageKind == ScsProfileStorageKind.SteamCloudRemote
            ? "Steam-Cloud-Profil aus Steam userdata: Saves können angezeigt werden. " +
              "Änderungen und Wiederherstellung sind bis zur sicheren Cloud-Freigabe gesperrt."
            : string.Empty;
        _ = LoadSaveEditorSavesAsync(value);
    }

    partial void OnSaveEditorSelectedSaveChanged(ScsSaveReference? value)
    {
        if (!_suppressSaveEditorAutomaticInspection)
        {
            ++_saveEditorSelectionGeneration;
            _ = InspectSaveEditorVehicleStateAsync(value);
        }
        NotifySaveEditorPlateAvailability();
    }

    partial void OnIsBusyChanged(bool value) => NotifySaveEditorPlateAvailability();

    private void NotifySaveEditorPlateAvailability()
    {
        OnPropertyChanged(nameof(SaveEditorCanEditPlates));
        SetSaveEditorTruckPlateCommand.NotifyCanExecuteChanged();
        SetSaveEditorTrailerPlateCommand.NotifyCanExecuteChanged();
    }

    partial void OnSaveEditorSelectedTruckChanged(ScsVehicleInventoryItem? value) =>
        SwitchSaveEditorTruckCommand.NotifyCanExecuteChanged();

    partial void OnSaveEditorSelectedTrailerChanged(ScsVehicleInventoryItem? value) =>
        SwitchSaveEditorTrailerCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private async Task RenameSaveEditorProfileAsync()
    {
        var profile = SaveEditorSelectedProfile;
        if (profile is null || _saveEditorProfileEditService is null)
        {
            SaveEditorStatusText = "Bitte zuerst ein Profil auswählen.";
            return;
        }

        if (RejectRemoteSteamCloudWrite())
            return;

        var newName = SaveEditorProfileNameText.Trim();
        var cloudWarning = profile.StorageKind == ScsProfileStorageKind.SteamCloud
            ? "\n\nSteam-Cloud-Profil: Steam darf während der Umbenennung nicht synchronisieren."
            : string.Empty;

        if (!_confirmationService.ConfirmSaveEdit(
                "Profil umbenennen",
                $"Profil '{profile.DisplayName}' in '{newName}' umbenennen? " +
                "profile.sii und der hex-codierte SCS-Profilordner werden gemeinsam aktualisiert." +
                cloudWarning,
                SaveEditConfirmationKind.ProfileRename))
        {
            return;
        }

        await ExecuteSaveEditorEditAsync(
            "Profilname",
            () => _saveEditorProfileEditService.RenameProfileAsync(profile, newName),
            refreshProfiles: true,
            exposeBackupForRestore: false);
    }

    [RelayCommand]
    private async Task SetSaveEditorMoneyAsync()
    {
        if (!TryParseLongInput(SaveEditorMoneyText, out var amount))
        {
            SaveEditorStatusText = "Geld muss eine nichtnegative ganze Zahl sein.";
            return;
        }

        await ExecuteSelectedSaveEditAsync(
            "Geld ändern",
            $"Geld im ausgewählten Save auf {amount} setzen?",
            () => _saveEditorProfileEditService!.SetMoneyAsync(SaveEditorSelectedSave!, amount));
    }

    [RelayCommand]
    private async Task SetSaveEditorExperienceAsync()
    {
        if (!TryParseLongInput(SaveEditorExperienceText, out var amount))
        {
            SaveEditorStatusText = "XP muss eine nichtnegative ganze Zahl sein.";
            return;
        }

        await ExecuteSelectedSaveEditAsync(
            "XP ändern",
            $"Erfahrungspunkte im ausgewählten Save auf {amount} setzen?",
            () => _saveEditorProfileEditService!.SetExperienceAsync(SaveEditorSelectedSave!, amount));
    }

    [RelayCommand]
    private async Task SetSaveEditorCareerSkillsAsync()
    {
        if (!TryParseSkillInput(SaveEditorAdrMaskText, 63, out var adr) ||
            !TryParseSkillInput(SaveEditorLongDistanceText, 6, out var longDistance) ||
            !TryParseSkillInput(SaveEditorHighValueCargoText, 6, out var highValue) ||
            !TryParseSkillInput(SaveEditorFragileCargoText, 6, out var fragile) ||
            !TryParseSkillInput(SaveEditorUrgentDeliveryText, 6, out var urgent) ||
            !TryParseSkillInput(SaveEditorEcoDrivingText, 6, out var eco))
        {
            SaveEditorStatusText =
                "Skills ungültig: ADR 0–63, alle anderen Skill-Level 0–6.";
            return;
        }

        var skills = new ScsCareerSkills(
            adr,
            longDistance,
            highValue,
            fragile,
            urgent,
            eco);

        await ExecuteSelectedSaveEditAsync(
            "Skills ändern",
            "Karriere-Skills im ausgewählten Save ändern?",
            () => _saveEditorProfileEditService!.SetCareerSkillsAsync(
                SaveEditorSelectedSave!,
                skills));
    }

    [RelayCommand]
    private async Task RepairSaveEditorTruckAsync() =>
        await ExecuteSelectedVehicleEditAsync(
            "Truck reparieren",
            "Alle unterstützten Verschleißwerte des aktiven Trucks auf 0 setzen?",
            () => _saveEditorVehicleEditService!.RepairActiveTruckAsync(SaveEditorSelectedSave!));

    [RelayCommand]
    private async Task SetSaveEditorFuelAsync()
    {
        if (!TryParseDecimalInput(SaveEditorFuelText, out var value) || value < 0m || value > 1m)
        {
            SaveEditorStatusText = "Kraftstoff muss zwischen 0 und 1 liegen, z. B. 0,75.";
            return;
        }

        await ExecuteSelectedVehicleEditAsync(
            "Kraftstoff ändern",
            $"Kraftstoffwert des aktiven Trucks auf {value} setzen?",
            () => _saveEditorVehicleEditService!.SetActiveTruckFuelAsync(SaveEditorSelectedSave!, value));
    }

    [RelayCommand]
    private async Task SetSaveEditorMileageAsync()
    {
        if (!TryParseDecimalInput(SaveEditorMileageText, out var value) ||
            value < 0m ||
            decimal.Truncate(value) != value)
        {
            SaveEditorStatusText = "Kilometerstand muss eine nichtnegative ganze Kilometerzahl sein.";
            return;
        }

        await ExecuteSelectedVehicleEditAsync(
            "Kilometerstand ändern",
            $"Kilometerstand des aktiven Trucks auf {value:0} km setzen?",
            () => _saveEditorVehicleEditService!.SetActiveTruckMileageAsync(SaveEditorSelectedSave!, value));
    }

    [RelayCommand]
    private async Task RepairSaveEditorTrailerAsync() =>
        await ExecuteSelectedVehicleEditAsync(
            "Trailer reparieren",
            "Alle unterstützten Verschleißwerte der aktiven Trailer-Kette auf 0 setzen?",
            () => _saveEditorVehicleEditService!.RepairActiveTrailerAsync(SaveEditorSelectedSave!));

    [RelayCommand]
    private async Task SetSaveEditorCargoMassAsync()
    {
        if (!TryParseDecimalInput(SaveEditorCargoMassText, out var value) || value < 0m)
        {
            SaveEditorStatusText = "Frachtgewicht muss eine nichtnegative Zahl sein.";
            return;
        }

        await ExecuteSelectedVehicleEditAsync(
            "Frachtgewicht ändern",
            $"cargo_mass der aktiven Trailer-Kette auf {value} setzen?",
            () => _saveEditorVehicleEditService!.SetActiveTrailerCargoMassAsync(SaveEditorSelectedSave!, value));
    }

    [RelayCommand(CanExecute = nameof(CanSwitchSaveEditorTruck))]
    private async Task SwitchSaveEditorTruckAsync()
    {
        var target = SaveEditorSelectedTruck;
        if (target is null || target.IsActive) return;

        await ExecuteSelectedVehicleEditAsync(
            "Truck wechseln",
            $"Aktiven Truck auf '{target.DisplayName ?? target.Id}' wechseln? Garage, Fahrer-Slot und HQ werden konsistent angepasst.",
            () => _saveEditorVehicleEditService!.SwitchActiveTruckAsync(SaveEditorSelectedSave!, target.Id));
    }

    [RelayCommand(CanExecute = nameof(CanSwitchSaveEditorTrailer))]
    private async Task SwitchSaveEditorTrailerAsync()
    {
        var target = SaveEditorSelectedTrailer;
        if (target is null || target.IsActive) return;

        await ExecuteSelectedVehicleEditAsync(
            "Trailer wechseln",
            $"Aktiven Trailer auf '{target.DisplayName ?? target.Id}' wechseln?",
            () => _saveEditorVehicleEditService!.SwitchActiveTrailerAsync(SaveEditorSelectedSave!, target.Id));
    }

    [RelayCommand(CanExecute = nameof(CanSetSaveEditorPlate))]
    private Task SetSaveEditorTruckPlateAsync() => SetSaveEditorPlateAsync(trailer: false);

    [RelayCommand(CanExecute = nameof(CanSetSaveEditorPlate))]
    private Task SetSaveEditorTrailerPlateAsync() => SetSaveEditorPlateAsync(trailer: true);

    private bool CanSetSaveEditorPlate() => SaveEditorCanEditPlates;

    private async Task SetSaveEditorPlateAsync(bool trailer)
    {
        if (!SaveEditorCanEditPlates)
            return;

        var save = SaveEditorSelectedSave!;
        var profile = SaveEditorSelectedProfile;
        var game = SaveEditorSelectedGame;
        var selectionGeneration = _saveEditorSelectionGeneration;
        var profileGeneration = _saveEditorProfileLoadGeneration;
        var inspectionGeneration = _saveEditorInspectionGeneration;
        var editor = trailer ? SaveEditorTrailerPlate : SaveEditorTruckPlate;
        var draft = editor.CaptureDraft();
        var countryCode = editor.CountryCode;
        var oppositeEditor = trailer ? SaveEditorTruckPlate : SaveEditorTrailerPlate;
        var oppositeDraft = oppositeEditor.CaptureDraft();
        var oppositeVehicleId = trailer ? _saveEditorActiveTruckId : _saveEditorActiveTrailerId;
        var writeSucceeded = false;

        bool SameContext() => game == SaveEditorSelectedGame &&
            selectionGeneration == _saveEditorSelectionGeneration &&
            profileGeneration == _saveEditorProfileLoadGeneration &&
            ReferenceEquals(profile, SaveEditorSelectedProfile) &&
            string.Equals(save.GameSiiPath, SaveEditorSelectedSave?.GameSiiPath, StringComparison.OrdinalIgnoreCase);

        await ExecuteSelectedVehicleEditAsync(
            trailer ? "Trailer-Kennzeichen ändern" : "Truck-Kennzeichen ändern",
            trailer ? $"Kennzeichen der aktiven Trailer-Kette auf '{draft.Text.Trim()}' setzen?"
                : $"Kennzeichen des aktiven Trucks auf '{draft.Text.Trim()}' setzen?",
            async () =>
            {
                var result = trailer
                    ? await _saveEditorVehicleEditService!.SetActiveTrailerLicensePlateAsync(
                        save, draft.Text, countryCode, draft.BackgroundRgb, draft.TextRgb)
                    : await _saveEditorVehicleEditService!.SetActiveTruckLicensePlateAsync(
                        save, draft.Text, countryCode, draft.BackgroundRgb, draft.TextRgb);
                writeSucceeded = true;
                return result;
            },
            () => SameContext() && inspectionGeneration == _saveEditorInspectionGeneration);

        // A plate-only reload refreshes the written value, but keeps the other vehicle's draft.
        // A new selection or active vehicle always takes precedence over an old draft.
        if (writeSucceeded && SameContext() && !SaveEditorIsInspecting &&
            oppositeVehicleId == (trailer ? _saveEditorActiveTruckId : _saveEditorActiveTrailerId))
            oppositeEditor.RestoreDraft(oppositeDraft);
    }

    [RelayCommand]
    private async Task SetSaveEditorEngineAsync()
    {
        var selected = SaveEditorSelectedEngine;
        if (selected is null)
        {
            SaveEditorStatusText = "Kein sicherer Motor aus dem aktuellen Save-Katalog ausgewählt.";
            return;
        }

        await ExecuteSelectedVehicleEditAsync(
            "Motor ändern",
            $"Motor auf '{selected.DataPath}' setzen?",
            () => _saveEditorVehicleEditService!.SetActiveTruckEngineAsync(
                SaveEditorSelectedSave!,
                selected.DataPath));
    }

    [RelayCommand]
    private async Task SetSaveEditorTransmissionAsync()
    {
        var selected = SaveEditorSelectedTransmission;
        if (selected is null)
        {
            SaveEditorStatusText = "Kein sicheres Getriebe aus dem aktuellen Save-Katalog ausgewählt.";
            return;
        }

        await ExecuteSelectedVehicleEditAsync(
            "Getriebe ändern",
            $"Getriebe auf '{selected.DataPath}' setzen?",
            () => _saveEditorVehicleEditService!.SetActiveTruckTransmissionAsync(
                SaveEditorSelectedSave!,
                selected.DataPath));
    }

    [RelayCommand]
    private async Task RestoreSaveEditorLastBackupAsync()
    {
        if (RejectRemoteSteamCloudWrite())
            return;

        if (_saveEditorSaveEditService is null ||
            string.IsNullOrWhiteSpace(_saveEditorLastEditedFilePath) ||
            SaveEditorLastBackupPath == "–")
        {
            SaveEditorStatusText = "In dieser Sitzung ist noch kein Save-Editor-Backup vorhanden.";
            return;
        }

        if (!_confirmationService.ConfirmSaveEdit(
                "Backup wiederherstellen",
                $"Letztes Backup wiederherstellen?\n{SaveEditorLastBackupPath}",
                SaveEditConfirmationKind.BackupRestore))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var writeMode = _saveEditorLastEditedSaveKind
                is ScsSaveKind.Manual or ScsSaveKind.QuickSave
                    ? ScsSaveWriteMode.AllowWhileGameRunning
                    : ScsSaveWriteMode.RequireGameStopped;

            await _saveEditorSaveEditService.RestoreBackupAsync(
                SaveEditorLastBackupPath,
                _saveEditorLastEditedFilePath,
                writeMode,
                _saveEditorLastEditedLastWriteTimeUtc);
            _saveEditorLastEditedLastWriteTimeUtc =
                File.GetLastWriteTimeUtc(_saveEditorLastEditedFilePath);
            await _logger.WriteAsync(
                "INFO",
                $"Save editor backup restored. Backup={SaveEditorLastBackupPath}; Target={_saveEditorLastEditedFilePath}");
            await ReloadCurrentSaveEditorSelectionAsync();
            // Keep an inspection failure visible instead of masking it as success.
            if (SaveEditorSelectedSave is not null &&
                SaveEditorStatusText.StartsWith("Save geladen ·", StringComparison.Ordinal))
            {
                SaveEditorStatusText = "Das letzte Save-Editor-Backup wurde wiederhergestellt.";
            }
        }
        catch (Exception ex)
        {
            SaveEditorStatusText = ex.Message;
            await _logger.WriteAsync("ERROR", "Save editor backup restore failed.", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSaveEditorSavesAsync(ScsProfileReference? profile)
    {
        var generation = ++_saveEditorProfileLoadGeneration;
        SaveEditorSaves.Clear();
        SaveEditorSelectedSave = null;
        ClearSaveEditorVehicleState();

        if (profile is null || _saveEditorLocator is null)
            return;

        bool IsCurrent() => generation == _saveEditorProfileLoadGeneration &&
            ReferenceEquals(profile, SaveEditorSelectedProfile) && profile.GameType == SaveEditorSelectedGame;

        try
        {
            var saves = await _saveEditorLocator.FindSavesAsync(profile);
            if (!IsCurrent()) return;
            foreach (var save in saves)
            {
                SaveEditorSaves.Add(save);
            }

            SaveEditorStatusText = saves.Count == 0
                ? $"Profil '{profile.DisplayName}' enthält keine Saves mit game.sii."
                : $"{saves.Count} Save(s) gefunden. Neuester Save wurde ausgewählt.";
            SaveEditorSelectedSave = saves.FirstOrDefault();
        }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            SaveEditorStatusText = "Saves konnten nicht gelesen werden. Details wurden protokolliert.";
            await _logger.WriteAsync("ERROR", "Save editor save discovery failed.", ex);
        }
    }

    private async Task InspectSaveEditorVehicleStateAsync(ScsSaveReference? save)
    {
        ClearSaveEditorVehicleState();
        var generation = _saveEditorInspectionGeneration;
        if (save is null || _saveEditorVehicleEditService is null)
            return;

        SaveEditorIsInspecting = true;
        try
        {
            await InspectSaveEditorVehicleStateCoreAsync(save, generation, _saveEditorVehicleEditService);
        }
        finally
        {
            if (generation == _saveEditorInspectionGeneration)
                SaveEditorIsInspecting = false;
        }
    }

    private async Task InspectSaveEditorVehicleStateCoreAsync(
        ScsSaveReference save, long generation, IScsVehicleEditService vehicleEditService)
    {

        bool IsCurrent() => generation == _saveEditorInspectionGeneration &&
            ReferenceEquals(save, SaveEditorSelectedSave) && save.GameType == SaveEditorSelectedGame;

        if (_saveEditorProfileEditService is not null)
        {
            try
            {
                var money = await _saveEditorProfileEditService.GetMoneyAsync(save);
                if (!IsCurrent()) return;
                var experience = await _saveEditorProfileEditService.GetExperienceAsync(save);
                if (!IsCurrent()) return;
                SaveEditorMoneyText = money.ToString(CultureInfo.CurrentCulture);
                SaveEditorExperienceText = experience.ToString(CultureInfo.CurrentCulture);
            }
            catch (ScsSaveEditException)
            {
                if (!IsCurrent()) return;
                SaveEditorMoneyText = string.Empty;
                SaveEditorExperienceText = string.Empty;
            }

            try
            {
                var skills = await _saveEditorProfileEditService.GetCareerSkillsAsync(save);
                if (!IsCurrent()) return;
                SaveEditorAdrMaskText = skills.AdrMask.ToString(CultureInfo.CurrentCulture);
                SaveEditorLongDistanceText = skills.LongDistance.ToString(CultureInfo.CurrentCulture);
                SaveEditorHighValueCargoText = skills.HighValueCargo.ToString(CultureInfo.CurrentCulture);
                SaveEditorFragileCargoText = skills.FragileCargo.ToString(CultureInfo.CurrentCulture);
                SaveEditorUrgentDeliveryText = skills.UrgentDelivery.ToString(CultureInfo.CurrentCulture);
                SaveEditorEcoDrivingText = skills.EcoDriving.ToString(CultureInfo.CurrentCulture);
            }
            catch (ScsSaveEditException)
            {
                if (!IsCurrent()) return;
                ClearSaveEditorCareerSkills();
            }
        }

        try
        {
            var state = await vehicleEditService.InspectActiveVehiclesAsync(save);
            if (!IsCurrent()) return;
            var inventory = await vehicleEditService.GetVehicleInventoryAsync(save);
            if (!IsCurrent()) return;

            SaveEditorTruckPlate.Load(state.TruckLicensePlate);
            SaveEditorTrailerPlate.Load(state.TrailerLicensePlate);
            _saveEditorActiveTruckId = state.TruckId;
            _saveEditorActiveTrailerId = state.TrailerId;

            SaveEditorActiveTruckText = inventory.Trucks
                .FirstOrDefault(item => item.IsActive)?.DisplayName ?? state.TruckId;
            SaveEditorActiveTrailerText = state.TrailerId is null
                ? "Kein aktiver Trailer"
                : inventory.Trailers.FirstOrDefault(item => item.IsActive)?.DisplayName
                  ?? state.TrailerId;
            SaveEditorFuelText = state.FuelRelative?.ToString(
                "0.############################",
                CultureInfo.CurrentCulture) ?? string.Empty;

            try
            {
                var mileage = await vehicleEditService.GetActiveTruckMileageAsync(save);
                if (!IsCurrent()) return;
                SaveEditorMileageText = mileage.ToString(
                    "0.############################",
                    CultureInfo.CurrentCulture);
            }
            catch (ScsSaveEditException)
            {
                if (!IsCurrent()) return;
                SaveEditorMileageText = string.Empty;
            }

            foreach (var truck in inventory.Trucks)
                SaveEditorTrucks.Add(truck);
            foreach (var trailer in inventory.Trailers)
                SaveEditorTrailers.Add(trailer);

            SaveEditorSelectedTruck =
                SaveEditorTrucks.FirstOrDefault(item => item.IsActive)
                ?? SaveEditorTrucks.FirstOrDefault();
            SaveEditorSelectedTrailer =
                SaveEditorTrailers.FirstOrDefault(item => item.IsActive)
                ?? SaveEditorTrailers.FirstOrDefault();

            try
            {
                var powertrain = await vehicleEditService.GetActiveTruckPowertrainAsync(save);
                if (!IsCurrent()) return;
                var catalog = await vehicleEditService.GetActiveTruckPowertrainCatalogAsync(save);
                if (!IsCurrent()) return;

                SaveEditorEnginePath = powertrain.EngineDataPath;
                SaveEditorTransmissionPath = powertrain.TransmissionDataPath;

                SaveEditorEngines.Clear();
                foreach (var item in catalog.Engines)
                    SaveEditorEngines.Add(item);

                SaveEditorTransmissions.Clear();
                foreach (var item in catalog.Transmissions)
                    SaveEditorTransmissions.Add(item);

                SaveEditorSelectedEngine =
                    SaveEditorEngines.FirstOrDefault(item => item.IsCurrent)
                    ?? SaveEditorEngines.FirstOrDefault();
                SaveEditorSelectedTransmission =
                    SaveEditorTransmissions.FirstOrDefault(item => item.IsCurrent)
                    ?? SaveEditorTransmissions.FirstOrDefault();
            }
            catch (ScsSaveEditException)
            {
                if (!IsCurrent()) return;
                SaveEditorEnginePath = string.Empty;
                SaveEditorTransmissionPath = string.Empty;
                SaveEditorEngines.Clear();
                SaveEditorTransmissions.Clear();
                SaveEditorSelectedEngine = null;
                SaveEditorSelectedTransmission = null;
            }

            SaveEditorStatusText =
                $"Save geladen · Truck: {SaveEditorActiveTruckText} · Trailer: {SaveEditorActiveTrailerText}";
        }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            SaveEditorStatusText =
                $"Save konnte nicht vollständig analysiert werden: {ex.Message}";
            await _logger.WriteAsync("WARN", "Save editor vehicle inspection failed.", ex);
        }
    }

    private bool RejectRemoteSteamCloudWrite()
    {
        var profile = SaveEditorSelectedProfile;
        if (profile is null ||
            (profile.StorageKind != ScsProfileStorageKind.SteamCloudRemote &&
             !ScsProfileSaveLocator.IsSteamCloudRemoteProfileDirectory(
                 profile.GameType, profile.ProfileDirectory)))
            return false;

        SaveEditorStatusText =
            "Steam-Cloud-Profil ist vorerst schreibgeschützt. " +
            "Direkte Änderungen in Steam userdata sind wegen der Cloud-Synchronisierung gesperrt.";
        return true;
    }

    private async Task ExecuteSelectedSaveEditAsync(
        string title,
        string confirmationMessage,
        Func<Task<ScsSaveEditResult>> operation)
    {
        if (SaveEditorSelectedSave is null || _saveEditorProfileEditService is null)
        {
            SaveEditorStatusText = "Bitte zuerst einen Save auswählen.";
            return;
        }

        if (RejectRemoteSteamCloudWrite())
            return;

        if (!_confirmationService.ConfirmSaveEdit(title, confirmationMessage))
            return;

        await ExecuteSaveEditorEditAsync(title, operation);
    }

    private async Task ExecuteSelectedVehicleEditAsync(
        string title,
        string confirmationMessage,
        Func<Task<ScsSaveEditResult>> operation,
        Func<bool>? selectionIsCurrent = null)
    {
        if (SaveEditorSelectedSave is null || _saveEditorVehicleEditService is null)
        {
            SaveEditorStatusText = "Bitte zuerst einen Save auswählen.";
            return;
        }

        if (RejectRemoteSteamCloudWrite())
            return;

        if (!_confirmationService.ConfirmSaveEdit(title, confirmationMessage))
            return;

        if (selectionIsCurrent is not null && !selectionIsCurrent())
        {
            SaveEditorStatusText = "Die Auswahl wurde während der Bestätigung geändert. Kennzeichen bitte erneut prüfen.";
            return;
        }

        await ExecuteSaveEditorEditAsync(title, operation);
    }

    private async Task ExecuteSaveEditorEditAsync(
        string operationName,
        Func<Task<ScsSaveEditResult>> operation,
        bool refreshProfiles = false,
        bool exposeBackupForRestore = true)
    {
        if (IsBusy || RejectRemoteSteamCloudWrite())
            return;

        IsBusy = true;
        var timer = Stopwatch.StartNew();
        SaveEditorStatusText = $"{operationName}: Save wird geprüft, gesichert und bearbeitet …";
        try
        {
            var editedSaveKind = SaveEditorSelectedSave?.Kind;
            var result = await operation();
            var editOperationMs = timer.ElapsedMilliseconds;

            if (exposeBackupForRestore)
            {
                SaveEditorLastBackupPath = result.BackupPath;
                _saveEditorLastEditedFilePath = result.FilePath;
                _saveEditorLastEditedSaveKind = editedSaveKind;
                _saveEditorLastEditedLastWriteTimeUtc =
                    File.GetLastWriteTimeUtc(result.FilePath);
                SaveEditorStatusText =
                    $"{operationName} abgeschlossen. Backup wurde automatisch erstellt.";
            }
            else
            {
                SaveEditorLastBackupPath = "–";
                _saveEditorLastEditedFilePath = null;
                _saveEditorLastEditedSaveKind = null;
                _saveEditorLastEditedLastWriteTimeUtc = null;
                SaveEditorStatusText =
                    $"{operationName} abgeschlossen. profile.sii und Profilordner wurden aktualisiert.";
            }

            await _logger.WriteAsync(
                "INFO",
                $"Save editor change completed. Operation={result.Operation}; File={result.FilePath}; Backup={result.BackupPath}; OriginalSha={result.OriginalSha256}; UpdatedSha={result.UpdatedSha256}");

            if (refreshProfiles)
            {
                await RefreshSaveEditorAsync();
            }
            else
            {
                await ReloadCurrentSaveEditorSelectionAsync();
                // The reload ordinarily displays "Save geladen", hiding the last edit.
                // Report success only if re-inspection succeeded; keep errors visible.
                if (SaveEditorSelectedSave is not null &&
                    SaveEditorStatusText.StartsWith("Save geladen ·", StringComparison.Ordinal))
                {
                    SaveEditorStatusText = exposeBackupForRestore
                        ? $"{operationName} erfolgreich gespeichert. Sicherheits-Backup wurde erstellt."
                        : $"{operationName} erfolgreich gespeichert.";
                }
            }

            await _logger.WriteAsync(
                "INFO",
                $"Save editor timing. Operation={operationName}; EditOperationMs={editOperationMs}; RefreshMs={timer.ElapsedMilliseconds - editOperationMs}; TotalMs={timer.ElapsedMilliseconds}");
        }
        catch (Exception ex)
        {
            await _logger.WriteAsync(
                "INFO",
                $"Save editor timing (failed). Operation={operationName}; ElapsedMs={timer.ElapsedMilliseconds}");
            SaveEditorStatusText = ex.Message;
            await _logger.WriteAsync("ERROR", $"Save editor operation failed: {operationName}", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadCurrentSaveEditorSelectionAsync()
    {
        var profile = SaveEditorSelectedProfile;
        var selectedSave = SaveEditorSelectedSave;
        var saveDirectory = selectedSave?.SaveDirectory;
        var profileGeneration = _saveEditorProfileLoadGeneration;
        var inspectionGeneration = _saveEditorInspectionGeneration;

        if (profile is null || _saveEditorLocator is null)
            return;

        var saves = await _saveEditorLocator.FindSavesAsync(profile);
        if (profileGeneration != _saveEditorProfileLoadGeneration ||
            inspectionGeneration != _saveEditorInspectionGeneration ||
            !ReferenceEquals(profile, SaveEditorSelectedProfile) ||
            !ReferenceEquals(selectedSave, SaveEditorSelectedSave))
            return;

        // Prevent a second, unawaited inspection triggered by the selection setter.
        _suppressSaveEditorAutomaticInspection = true;
        try
        {
            SaveEditorSaves.Clear();
            foreach (var save in saves)
                SaveEditorSaves.Add(save);
            SaveEditorSelectedSave =
                saves.FirstOrDefault(item =>
                    string.Equals(
                        item.SaveDirectory,
                        saveDirectory,
                        StringComparison.OrdinalIgnoreCase))
                ?? saves.FirstOrDefault();
        }
        finally
        {
            _suppressSaveEditorAutomaticInspection = false;
        }

        if (SaveEditorSelectedSave is null)
        {
            ClearSaveEditorVehicleState();
            SaveEditorStatusText =
                "Der Schreibvorgang wurde abgeschlossen, der Save konnte danach jedoch nicht erneut gefunden werden.";
            return;
        }

        await InspectSaveEditorVehicleStateAsync(SaveEditorSelectedSave);
    }

    private void ClearSaveEditorSaveState()
    {
        SaveEditorSaves.Clear();
        SaveEditorSelectedSave = null;
        ClearSaveEditorVehicleState();
    }

    private void ClearSaveEditorVehicleState()
    {
        ++_saveEditorInspectionGeneration;
        SaveEditorIsInspecting = false;
        _saveEditorActiveTruckId = null;
        _saveEditorActiveTrailerId = null;
        SaveEditorTruckPlate.Reset(SaveEditorSelectedGame);
        SaveEditorTrailerPlate.Reset(SaveEditorSelectedGame);
        SaveEditorTrucks.Clear();
        SaveEditorTrailers.Clear();
        SaveEditorSelectedTruck = null;
        SaveEditorSelectedTrailer = null;
        SaveEditorActiveTruckText = "–";
        SaveEditorActiveTrailerText = "–";
        SaveEditorMoneyText = string.Empty;
        SaveEditorExperienceText = string.Empty;
        SaveEditorFuelText = string.Empty;
        SaveEditorMileageText = string.Empty;
        ClearSaveEditorCareerSkills();
        SaveEditorCargoMassText = string.Empty;
        SaveEditorEnginePath = string.Empty;
        SaveEditorTransmissionPath = string.Empty;
        SaveEditorEngines.Clear();
        SaveEditorTransmissions.Clear();
        SaveEditorSelectedEngine = null;
        SaveEditorSelectedTransmission = null;
    }

    private bool CanSwitchSaveEditorTruck() =>
        SaveEditorSelectedTruck is { IsActive: false } &&
        SaveEditorSelectedSave is not null;

    private bool CanSwitchSaveEditorTrailer() =>
        SaveEditorSelectedTrailer is { IsActive: false } &&
        SaveEditorSelectedSave is not null;

    private void ClearSaveEditorCareerSkills()
    {
        SaveEditorAdrMaskText = string.Empty;
        SaveEditorLongDistanceText = string.Empty;
        SaveEditorHighValueCargoText = string.Empty;
        SaveEditorFragileCargoText = string.Empty;
        SaveEditorUrgentDeliveryText = string.Empty;
        SaveEditorEcoDrivingText = string.Empty;
    }

    private static bool TryParseSkillInput(
        string text,
        int max,
        out int value)
    {
        if (int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out value) ||
            int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value))
        {
            return value >= 0 && value <= max;
        }

        value = 0;
        return false;
    }

    private static bool TryParseLongInput(string text, out long value)
    {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) ||
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return value >= 0;
        }

        value = 0;
        return false;
    }

    private static bool TryParseDecimalInput(string text, out decimal value) =>
        decimal.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.CurrentCulture,
            out value)
        || decimal.TryParse(
            text,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);
}
