using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Core.Tests;

public sealed class ScsLicensePlateValueTests
{
    [Theory]
    [InlineData("\"YN16 MSO|uk\"", "YN16 MSO", "uk")]
    [InlineData("\"NMC 123|texas\"", "NMC 123", "texas")]
    [InlineData("\"MOD-42|promods_region\"", "MOD-42", "promods_region")]
    [InlineData("\"BEL 123|belgium\"", "BEL 123", "belgium")]
    public void NativeAndMapModPlatesPreserveTextAndCode(string scalar, string text, string country)
    {
        var plate = Assert.IsType<ScsLicensePlateValue>(ScsLicensePlateValue.Parse(scalar));
        Assert.Equal(text, plate.Text);
        Assert.Equal(country, plate.CountryCode);
    }

    [Fact]
    public void GermanMaterialPreservesSeparationBetweenDistrictAndRemainder()
    {
        var plate = ScsLicensePlateValue.Parse("\"<font xscale=1.00 yscale=1.25>SI<offset hshift=4 vshift=-2.5><img src=/material/ui/lp/germany/b.mat color=FFFFFFFF><offset hshift=4 vshift=2>AA 998</font>|germany\"");
        Assert.Equal("SI AA 998", plate?.Text);
        Assert.Equal("germany", plate?.CountryCode);
    }

    [Fact]
    public void ExistingCustomPlateIsRecognizedBeforeBelgiumSuffix()
    {
        var plate = ScsLicensePlateValue.Parse("\"<color value=ff12abef><margin left=-15><img src=/material/ui/white.mat xscale=stretch yscale=stretch><ret><margin left=2><align hstyle=left vstyle=center><font xscale=1 yscale=1><color value=ff345678>NMC-42</align></margin>|belgium\"");
        Assert.Equal("custom", plate?.CountryCode);
        Assert.Equal("NMC-42", plate?.Text);
        Assert.Equal("12abef", plate?.BackgroundRgb);
        Assert.Equal("345678", plate?.TextRgb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\"\"")]
    public void MissingPlatesDoNotInventDefaults(string? scalar) =>
        Assert.Null(ScsLicensePlateValue.Parse(scalar));

    [Theory]
    [InlineData("\"<unknown>AB 123</unknown>|mod_region\"", "mod_region")]
    [InlineData("\"<color value=ff123456>AB 123|belgium\"", "belgium")]
    [InlineData("\"AB 123|bad/code\"", "")]
    [InlineData("\"AB 123\"", "")]
    [InlineData("\"AB 123|texas|germany\"", "")]
    public void UnsupportedFormatsKeepRawValueWithoutGuessingEditableText(string scalar, string country)
    {
        var plate = Assert.IsType<ScsLicensePlateValue>(ScsLicensePlateValue.Parse(scalar));
        Assert.Equal(scalar[1..^1], plate.RawValue);
        Assert.Equal(country, plate.CountryCode);
        Assert.Null(plate.Text);
    }
}
