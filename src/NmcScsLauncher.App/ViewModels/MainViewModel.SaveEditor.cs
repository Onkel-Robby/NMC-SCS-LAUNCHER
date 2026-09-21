using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.ViewModels;

public partial class MainViewModel
{
    private IScsProfileSaveLocator? _saveEditorLocator;
    private IScsProfileEditService? _saveEditorProfileEditService;
    private IScsVehicleEditService? _saveEditorVehicleEditService;
    private IScsSaveEditService? _saveEditorSaveEditService;
    private string? _saveEditorLastEditedFilePath;

    [ObservableProperty] private GameType _saveEditorSelectedGame = GameType.Ets2;
    [ObservableProperty] private ScsProfileReference? _saveEditorSelectedProfile;
    [ObservableProperty] private ScsSaveReference? _saveEditorSelectedSave;
    [ObservableProperty] private ScsVehicleInventoryItem? _saveEditorSelectedTruck;
    [ObservableProperty] private ScsVehicleInventoryItem? _saveEditorSelectedTrailer;
    [ObservableProperty] private string _saveEditorStatusText = "Spiel auswählen und Profile laden.";
    [ObservableProperty] private string _saveEditorGameHome = "–";
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
    [ObservableProperty] private string _saveEditorTruckPlateText = string.Empty;
    [ObservableProperty] private string _saveEditorTrailerPlateText = string.Empty;
    [ObservableProperty] private string _saveEditorPlateCountry = "germany";
    [ObservableProperty] private string _saveEditorPlateBackgroundRgb = "ffffff";
    [ObservableProperty] private string _saveEditorPlateTextRgb = "000000";
    [ObservableProperty] private string _saveEditorEnginePath = string.Empty;
    [ObservableProperty] private string _saveEditorTransmissionPath = string.Empty;
    [ObservableProperty] private string _saveEditorActiveTruckText = "–";
    [ObservableProperty] private string _saveEditorActiveTrailerText = "–";
    [ObservableProperty] private string _saveEditorLastBackupPath = "–";

    public IReadOnlyList<GameType> SaveEditorGames { get; } = Enum.GetValues<GameType>();
    public ObservableCollection<ScsProfileReference> SaveEditorProfiles { get; } = new();
    public ObservableCollection<ScsSaveReference> SaveEditorSaves { get; } = new();
    public ObservableCollection<ScsVehicleInventoryItem> SaveEditorTrucks { get; } = new();
    public ObservableCollection<ScsVehicleInventoryItem> SaveEditorTrailers { get; } = new();

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

        IsBusy = true;
        try
        {
            var home = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                GameDefinition.For(SaveEditorSelectedGame).HomeDirectoryName);

            SaveEditorGameHome = home;
            var previousProfilePath = SaveEditorSelectedProfile?.ProfileDirectory;
            var profiles = await _saveEditorLocator.FindProfilesAsync(
                SaveEditorSelectedGame,
                home);

            SaveEditorProfiles.Clear();
            foreach (var profile in profiles)
            {
                SaveEditorProfiles.Add(profile);
            }

            SaveEditorSelectedProfile =
                profiles.FirstOrDefault(item =>
                    string.Equals(
                        item.ProfileDirectory,
                        previousProfilePath,
                        StringComparison.OrdinalIgnoreCase))
                ?? profiles.FirstOrDefault();

            if (SaveEditorSelectedProfile is null)
            {
                ClearSaveEditorSaveState();
                SaveEditorStatusText =
                    $"{GameDefinition.For(SaveEditorSelectedGame).DisplayName}: keine Profile mit profile.sii gefunden.";
            }
            else
            {
                SaveEditorStatusText =
                    $"{profiles.Count} Profil(e) gefunden. Profil und Save auswählen.";
            }
        }
        catch (Exception ex)
        {
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

    partial void OnSaveEditorSelectedGameChanged(GameType value)
    {
        SaveEditorProfiles.Clear();
        SaveEditorSelectedProfile = null;
        ClearSaveEditorSaveState();
        SaveEditorGameHome = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            GameDefinition.For(value).HomeDirectoryName);
        SaveEditorStatusText =
            $"{GameDefinition.For(value).DisplayName}: auf „Profile laden“ klicken.";
    }

    partial void OnSaveEditorSelectedProfileChanged(ScsProfileReference? value)
    {
        SaveEditorProfileNameText = value?.DisplayName ?? string.Empty;
        _ = LoadSaveEditorSavesAsync(value);
    }

    partial void OnSaveEditorSelectedSaveChanged(ScsSaveReference? value)
    {
        _ = InspectSaveEditorVehicleStateAsync(value);
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

        var newName = SaveEditorProfileNameText.Trim();
        if (!_confirmationService.ConfirmSaveEdit(
                "Profil umbenennen",
                $"Profil '{profile.DisplayName}' in '{newName}' umbenennen?"))
        {
            return;
        }

        await ExecuteSaveEditorEditAsync(
            "Profilname",
            () => _saveEditorProfileEditService.RenameProfileAsync(profile, newName),
            refreshProfiles: true);
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
        if (!TryParseDecimalInput(SaveEditorMileageText, out var value) || value < 0m)
        {
            SaveEditorStatusText = "Kilometerstand muss eine nichtnegative Zahl sein.";
            return;
        }

        await ExecuteSelectedVehicleEditAsync(
            "Kilometerstand ändern",
            $"Kilometerstand des aktiven Trucks auf {value} km setzen?",
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
            $"Aktiven Truck auf '{target.Id}' wechseln? Garage, Fahrer-Slot und HQ werden konsistent angepasst.",
            () => _saveEditorVehicleEditService!.SwitchActiveTruckAsync(SaveEditorSelectedSave!, target.Id));
    }

    [RelayCommand(CanExecute = nameof(CanSwitchSaveEditorTrailer))]
    private async Task SwitchSaveEditorTrailerAsync()
    {
        var target = SaveEditorSelectedTrailer;
        if (target is null || target.IsActive) return;

        await ExecuteSelectedVehicleEditAsync(
            "Trailer wechseln",
            $"Aktiven Trailer auf '{target.Id}' wechseln?",
            () => _saveEditorVehicleEditService!.SwitchActiveTrailerAsync(SaveEditorSelectedSave!, target.Id));
    }

    [RelayCommand]
    private async Task SetSaveEditorTruckPlateAsync()
    {
        await ExecuteSelectedVehicleEditAsync(
            "Truck-Kennzeichen ändern",
            $"Kennzeichen des aktiven Trucks auf '{SaveEditorTruckPlateText.Trim()}' setzen?",
            () => _saveEditorVehicleEditService!.SetActiveTruckLicensePlateAsync(
                SaveEditorSelectedSave!,
                SaveEditorTruckPlateText,
                SaveEditorPlateCountry,
                SaveEditorPlateBackgroundRgb,
                SaveEditorPlateTextRgb));
    }

    [RelayCommand]
    private async Task SetSaveEditorTrailerPlateAsync()
    {
        await ExecuteSelectedVehicleEditAsync(
            "Trailer-Kennzeichen ändern",
            $"Kennzeichen der aktiven Trailer-Kette auf '{SaveEditorTrailerPlateText.Trim()}' setzen?",
            () => _saveEditorVehicleEditService!.SetActiveTrailerLicensePlateAsync(
                SaveEditorSelectedSave!,
                SaveEditorTrailerPlateText,
                SaveEditorPlateCountry,
                SaveEditorPlateBackgroundRgb,
                SaveEditorPlateTextRgb));
    }

    [RelayCommand]
    private async Task SetSaveEditorEngineAsync()
    {
        await ExecuteSelectedVehicleEditAsync(
            "Motor ändern",
            $"Engine-data_path auf '{SaveEditorEnginePath.Trim()}' setzen?",
            () => _saveEditorVehicleEditService!.SetActiveTruckEngineAsync(
                SaveEditorSelectedSave!,
                SaveEditorEnginePath));
    }

    [RelayCommand]
    private async Task SetSaveEditorTransmissionAsync()
    {
        await ExecuteSelectedVehicleEditAsync(
            "Getriebe ändern",
            $"Transmission-data_path auf '{SaveEditorTransmissionPath.Trim()}' setzen?",
            () => _saveEditorVehicleEditService!.SetActiveTruckTransmissionAsync(
                SaveEditorSelectedSave!,
                SaveEditorTransmissionPath));
    }

    [RelayCommand]
    private async Task RestoreSaveEditorLastBackupAsync()
    {
        if (_saveEditorSaveEditService is null ||
            string.IsNullOrWhiteSpace(_saveEditorLastEditedFilePath) ||
            SaveEditorLastBackupPath == "–")
        {
            SaveEditorStatusText = "In dieser Sitzung ist noch kein Save-Editor-Backup vorhanden.";
            return;
        }

        if (!_confirmationService.ConfirmSaveEdit(
                "Backup wiederherstellen",
                $"Letztes Backup wiederherstellen?\n{SaveEditorLastBackupPath}"))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _saveEditorSaveEditService.RestoreBackupAsync(
                SaveEditorLastBackupPath,
                _saveEditorLastEditedFilePath);
            SaveEditorStatusText = "Das letzte Save-Editor-Backup wurde wiederhergestellt.";
            await _logger.WriteAsync(
                "INFO",
                $"Save editor backup restored. Backup={SaveEditorLastBackupPath}; Target={_saveEditorLastEditedFilePath}");
            await ReloadCurrentSaveEditorSelectionAsync();
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
        SaveEditorSaves.Clear();
        SaveEditorSelectedSave = null;
        ClearSaveEditorVehicleState();

        if (profile is null || _saveEditorLocator is null)
            return;

        try
        {
            var saves = await _saveEditorLocator.FindSavesAsync(profile);
            foreach (var save in saves)
            {
                SaveEditorSaves.Add(save);
            }

            SaveEditorSelectedSave = saves.FirstOrDefault();
            SaveEditorStatusText = saves.Count == 0
                ? $"Profil '{profile.DisplayName}' enthält keine Saves mit game.sii."
                : $"{saves.Count} Save(s) gefunden. Neuester Save wurde ausgewählt.";
        }
        catch (Exception ex)
        {
            SaveEditorStatusText = "Saves konnten nicht gelesen werden. Details wurden protokolliert.";
            await _logger.WriteAsync("ERROR", "Save editor save discovery failed.", ex);
        }
    }

    private async Task InspectSaveEditorVehicleStateAsync(ScsSaveReference? save)
    {
        ClearSaveEditorVehicleState();
        if (save is null || _saveEditorVehicleEditService is null)
            return;

        if (_saveEditorProfileEditService is not null)
        {
            try
            {
                var skills = await _saveEditorProfileEditService.GetCareerSkillsAsync(save);
                SaveEditorAdrMaskText = skills.AdrMask.ToString(CultureInfo.CurrentCulture);
                SaveEditorLongDistanceText = skills.LongDistance.ToString(CultureInfo.CurrentCulture);
                SaveEditorHighValueCargoText = skills.HighValueCargo.ToString(CultureInfo.CurrentCulture);
                SaveEditorFragileCargoText = skills.FragileCargo.ToString(CultureInfo.CurrentCulture);
                SaveEditorUrgentDeliveryText = skills.UrgentDelivery.ToString(CultureInfo.CurrentCulture);
                SaveEditorEcoDrivingText = skills.EcoDriving.ToString(CultureInfo.CurrentCulture);
            }
            catch (ScsSaveEditException)
            {
                ClearSaveEditorCareerSkills();
            }
        }

        try
        {
            var state = await _saveEditorVehicleEditService.InspectActiveVehiclesAsync(save);
            var inventory = await _saveEditorVehicleEditService.GetVehicleInventoryAsync(save);

            SaveEditorActiveTruckText = state.TruckId;
            SaveEditorActiveTrailerText = state.TrailerId ?? "Kein aktiver Trailer";
            SaveEditorFuelText = state.FuelRelative?.ToString(
                "0.############################",
                CultureInfo.CurrentCulture) ?? string.Empty;

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
                var powertrain = await _saveEditorVehicleEditService.GetActiveTruckPowertrainAsync(save);
                SaveEditorEnginePath = powertrain.EngineDataPath;
                SaveEditorTransmissionPath = powertrain.TransmissionDataPath;
            }
            catch (ScsSaveEditException)
            {
                SaveEditorEnginePath = string.Empty;
                SaveEditorTransmissionPath = string.Empty;
            }

            SaveEditorStatusText =
                $"Save geladen · Truck: {state.TruckId} · Trailer: {state.TrailerId ?? "–"}";
        }
        catch (Exception ex)
        {
            SaveEditorStatusText =
                $"Save konnte nicht vollständig analysiert werden: {ex.Message}";
            await _logger.WriteAsync("WARN", "Save editor vehicle inspection failed.", ex);
        }
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

        if (!_confirmationService.ConfirmSaveEdit(title, confirmationMessage))
            return;

        await ExecuteSaveEditorEditAsync(title, operation);
    }

    private async Task ExecuteSelectedVehicleEditAsync(
        string title,
        string confirmationMessage,
        Func<Task<ScsSaveEditResult>> operation)
    {
        if (SaveEditorSelectedSave is null || _saveEditorVehicleEditService is null)
        {
            SaveEditorStatusText = "Bitte zuerst einen Save auswählen.";
            return;
        }

        if (!_confirmationService.ConfirmSaveEdit(title, confirmationMessage))
            return;

        await ExecuteSaveEditorEditAsync(title, operation);
    }

    private async Task ExecuteSaveEditorEditAsync(
        string operationName,
        Func<Task<ScsSaveEditResult>> operation,
        bool refreshProfiles = false)
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            var result = await operation();
            SaveEditorLastBackupPath = result.BackupPath;
            _saveEditorLastEditedFilePath = result.FilePath;
            SaveEditorStatusText =
                $"{operationName} abgeschlossen. Backup wurde automatisch erstellt.";
            await _logger.WriteAsync(
                "INFO",
                $"Save editor change completed. Operation={result.Operation}; File={result.FilePath}; Backup={result.BackupPath}; OriginalSha={result.OriginalSha256}; UpdatedSha={result.UpdatedSha256}");

            if (refreshProfiles)
                await RefreshSaveEditorAsync();
            else
                await ReloadCurrentSaveEditorSelectionAsync();
        }
        catch (Exception ex)
        {
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
        var saveDirectory = SaveEditorSelectedSave?.SaveDirectory;

        if (profile is null || _saveEditorLocator is null)
            return;

        var saves = await _saveEditorLocator.FindSavesAsync(profile);
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

        if (SaveEditorSelectedSave is not null)
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
        SaveEditorTrucks.Clear();
        SaveEditorTrailers.Clear();
        SaveEditorSelectedTruck = null;
        SaveEditorSelectedTrailer = null;
        SaveEditorActiveTruckText = "–";
        SaveEditorActiveTrailerText = "–";
        SaveEditorFuelText = string.Empty;
        SaveEditorMileageText = string.Empty;
        ClearSaveEditorCareerSkills();
        SaveEditorCargoMassText = string.Empty;
        SaveEditorEnginePath = string.Empty;
        SaveEditorTransmissionPath = string.Empty;
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
