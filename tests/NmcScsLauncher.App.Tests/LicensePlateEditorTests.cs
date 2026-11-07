using NmcScsLauncher.App.ViewModels;
using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.App.Tests;

public sealed class LicensePlateEditorTests
{
    internal const string CustomPlate = "\"<color value=ff123abc><margin left=-15>" +
        "<img src=/material/ui/white.mat xscale=stretch yscale=stretch><ret>" +
        "<margin left=2><align hstyle=left vstyle=center><font xscale=1 yscale=1>" +
        "<color value=ff987def>CUSTOM 1</align></margin>|belgium\"";

    [Theory]
    [InlineData(GameType.Ets2, "\"YN16 MSO|uk\"", "uk", "YN16 MSO", false)]
    [InlineData(GameType.Ats, "\"TX 123|texas\"", "texas", "TX 123", false)]
    [InlineData(GameType.Ets2, "\"MAP 123|promods_region\"", "promods_region", "MAP 123", true)]
    [InlineData(GameType.Ats, "\"MAP 456|canada_mod\"", "canada_mod", "MAP 456", true)]
    public void LoadingPreservesNativeAndUnlistedRegions(GameType game, string raw, string code, string text, bool other)
    {
        var editor = new LicensePlateEditorViewModel(game);
        editor.Load(raw);

        Assert.Equal(code, editor.CountryCode);
        Assert.Equal(text, editor.Text);
        Assert.Equal(other, editor.UsesOtherCountry);
        Assert.Equal(other ? code : string.Empty, editor.OtherCountryCode);
        Assert.Empty(editor.LoadWarning);
    }

    [Theory]
    [InlineData(GameType.Ets2)]
    [InlineData(GameType.Ats)]
    public void LoadingCustomPlateRestoresColorsWithoutSelectingBelgium(GameType game)
    {
        var editor = new LicensePlateEditorViewModel(game);
        editor.Load(CustomPlate);

        Assert.Equal("CUSTOM 1", editor.Text);
        Assert.Equal("custom", editor.CountryCode);
        Assert.True(editor.UsesCustomColors);
        Assert.False(editor.UsesOtherCountry);
        Assert.Equal("123abc", editor.BackgroundRgb);
        Assert.Equal("987def", editor.TextRgb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"\"")]
    [InlineData("unreadable")]
    [InlineData("\"ABC|invalid code\"")]
    public void MissingOrUnreadablePlateClearsPriorValuesWithoutDefaultCountry(string? raw)
    {
        var editor = new LicensePlateEditorViewModel(GameType.Ets2);
        editor.Load(CustomPlate);
        editor.Load(raw);

        Assert.Empty(editor.Text);
        Assert.Empty(editor.CountryCode);
        Assert.Null(editor.SelectedRegion);
        Assert.Equal("ffffff", editor.BackgroundRgb);
        Assert.Equal("000000", editor.TextRgb);
    }

    [Fact]
    public void UnsupportedMarkupPreservesMapCodeAndRequiresNewText()
    {
        var editor = new LicensePlateEditorViewModel(GameType.Ets2);
        editor.Load("\"<unrecognized>MAP</unrecognized>|map_region\"");

        Assert.Equal("map_region", editor.CountryCode);
        Assert.True(editor.UsesOtherCountry);
        Assert.Empty(editor.Text);
        Assert.NotEmpty(editor.LoadWarning);
        Assert.Equal("<unrecognized>MAP</unrecognized>|map_region", editor.SavedValue);

        editor.Reset(GameType.Ats);

        Assert.Empty(editor.SavedValue);
    }

    [Fact]
    public void RegionSelectionReplacesOtherCodeAndNotifiesVisibility()
    {
        var editor = new LicensePlateEditorViewModel(GameType.Ats);
        var changed = new List<string?>();
        editor.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        editor.Load("\"MAP 1|map_region\"");
        editor.OtherCountryCode = "  edited_region  ";
        Assert.Equal("edited_region", editor.CountryCode);

        editor.SelectedRegion = editor.Regions.Single(region => region.Code == "texas");

        Assert.Equal("texas", editor.CountryCode);
        Assert.False(editor.UsesOtherCountry);
        Assert.Contains(nameof(editor.UsesOtherCountry), changed);
        Assert.Contains(nameof(editor.CountryCode), changed);
    }
}
