using System.Text.RegularExpressions;

namespace NmcScsLauncher.Core;

/// <summary>Read-only representation of a saved plate; null text means unsupported markup.</summary>
public sealed record ScsLicensePlateValue(
    string RawValue,
    string? Text,
    string CountryCode,
    string BackgroundRgb,
    string TextRgb)
{
    private static readonly Regex CountryPattern = new(@"\A[a-zA-Z0-9_]{2,32}\z", RegexOptions.NonBacktracking);
    private static readonly Regex TextPattern = new(@"\A[A-Za-z0-9 -]{1,20}\z", RegexOptions.NonBacktracking);
    private static readonly Regex GermanPattern = new(
        @"\A<font xscale=1\.00 yscale=1\.25>([A-Za-z]{1,3})<offset hshift=4 vshift=-2\.5><img src=/material/ui/lp/germany/b\.mat color=FFFFFFFF><offset hshift=4 vshift=2>([A-Za-z0-9 -]+)</font>\z",
        RegexOptions.NonBacktracking);
    private static readonly Regex CustomPattern = new(
        @"\A<color value=ff([0-9a-fA-F]{6})><margin left=-15><img src=/material/ui/white\.mat xscale=stretch yscale=stretch><ret><margin left=2><align hstyle=left vstyle=center><font xscale=1 yscale=1><color value=ff([0-9a-fA-F]{6})>([A-Za-z0-9 -]{1,20})</align></margin>\z",
        RegexOptions.NonBacktracking);

    public static ScsLicensePlateValue? Parse(string? scalar)
    {
        if (string.IsNullOrWhiteSpace(scalar))
            return null;

        var raw = scalar.Trim();
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            raw = raw[1..^1];
        if (raw.Length == 0)
            return null;

        var unknown = new ScsLicensePlateValue(raw, null, string.Empty, "ffffff", "000000");
        var separator = raw.IndexOf('|');
        if (separator < 0 || separator != raw.LastIndexOf('|'))
            return unknown;

        var country = raw[(separator + 1)..];
        if (!CountryPattern.IsMatch(country))
            return unknown;
        country = country.ToLowerInvariant();
        var body = raw[..separator];

        // The existing writer uses Belgium's backing definition for Custom plates.
        // Recognize only that complete format; never strip arbitrary map-mod markup.
        var custom = country == "belgium" ? CustomPattern.Match(body) : Match.Empty;
        if (custom.Success)
        {
            return new ScsLicensePlateValue(raw, custom.Groups[3].Value.Trim(), "custom",
                custom.Groups[1].Value.ToLowerInvariant(), custom.Groups[2].Value.ToLowerInvariant());
        }

        if (country == "germany")
        {
            var german = GermanPattern.Match(body);
            if (german.Success)
                body = $"{german.Groups[1].Value} {german.Groups[2].Value}";
        }

        return unknown with
        {
            CountryCode = country,
            Text = TextPattern.IsMatch(body) && !string.IsNullOrWhiteSpace(body) ? body.Trim() : null
        };
    }
}
