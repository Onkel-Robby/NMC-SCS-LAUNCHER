using CommunityToolkit.Mvvm.ComponentModel;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.ViewModels;

public partial class LicensePlateEditorViewModel : ObservableObject
{
    private GameType _game;
    [ObservableProperty] private IReadOnlyList<ScsPlateRegion> _regions = [];
    [ObservableProperty] private ScsPlateRegion? _selectedRegion;
    [ObservableProperty] private string _otherCountryCode = string.Empty;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private string _backgroundRgb = "ffffff";
    [ObservableProperty] private string _textRgb = "000000";
    [ObservableProperty] private string _loadWarning = string.Empty;
    private string _savedValue = string.Empty;

    public string SavedValue
    {
        get => _savedValue;
        private set => SetProperty(ref _savedValue, value);
    }

    public bool UsesOtherCountry => SelectedRegion?.Code == ScsPlateRegionCatalog.OtherCode;
    public bool UsesCustomColors => SelectedRegion?.Code == "custom";
    public string CountryCode => UsesOtherCountry ? OtherCountryCode.Trim() : SelectedRegion?.Code ?? string.Empty;

    public LicensePlateEditorViewModel(GameType game) => Reset(game);

    public void Reset(GameType game)
    {
        _game = game;
        Regions = ScsPlateRegionCatalog.For(game);
        SelectedRegion = null;
        OtherCountryCode = string.Empty;
        Text = string.Empty;
        BackgroundRgb = "ffffff";
        TextRgb = "000000";
        LoadWarning = string.Empty;
        SavedValue = string.Empty;
    }

    public void Load(string? scalar)
    {
        Reset(_game);
        var plate = ScsLicensePlateValue.Parse(scalar);
        if (plate is null)
            return;

        SavedValue = plate.RawValue;
        Text = plate.Text ?? string.Empty;
        BackgroundRgb = plate.BackgroundRgb;
        TextRgb = plate.TextRgb;
        if (plate.CountryCode.Length > 0)
        {
            SelectedRegion = Regions.FirstOrDefault(region =>
                string.Equals(region.Code, plate.CountryCode, StringComparison.OrdinalIgnoreCase));
            if (SelectedRegion is null)
            {
                OtherCountryCode = plate.CountryCode;
                SelectedRegion = Regions.Single(region => region.Code == ScsPlateRegionCatalog.OtherCode);
            }
        }

        if (plate.Text is null || plate.CountryCode.Length == 0)
            LoadWarning = "Das vorhandene Kennzeichen konnte nicht vollständig gelesen werden. Text und Region vor dem Speichern prüfen.";
    }

    internal sealed record Draft(ScsPlateRegion? Region, string OtherCode, string Text,
        string BackgroundRgb, string TextRgb, string SavedValue, string LoadWarning);

    internal Draft CaptureDraft() =>
        new(SelectedRegion, OtherCountryCode, Text, BackgroundRgb, TextRgb, SavedValue, LoadWarning);

    internal void RestoreDraft(Draft draft)
    {
        SelectedRegion = draft.Region;
        OtherCountryCode = draft.OtherCode;
        Text = draft.Text;
        BackgroundRgb = draft.BackgroundRgb;
        TextRgb = draft.TextRgb;
        SavedValue = draft.SavedValue;
        LoadWarning = draft.LoadWarning;
    }

    partial void OnSelectedRegionChanged(ScsPlateRegion? value)
    {
        OnPropertyChanged(nameof(UsesOtherCountry));
        OnPropertyChanged(nameof(UsesCustomColors));
        OnPropertyChanged(nameof(CountryCode));
    }

    partial void OnOtherCountryCodeChanged(string value) => OnPropertyChanged(nameof(CountryCode));
}
