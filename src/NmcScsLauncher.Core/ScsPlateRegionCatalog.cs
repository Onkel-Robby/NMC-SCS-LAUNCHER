namespace NmcScsLauncher.Core;

/// <summary>
/// Human-readable shortcuts for common native SCS plate country/state identifiers.
/// The installed DLC and map mods can change which definitions are available;
/// "Anderer Code" deliberately preserves support for unlisted regions.
/// </summary>
public sealed record ScsPlateRegion(string Name, string Code)
{
    public string DisplayName => Code == ScsPlateRegionCatalog.OtherCode
        ? Name
        : $"{Name} ({Code})";
}

public static class ScsPlateRegionCatalog
{
    public const string OtherCode = "__other";

    private static readonly IReadOnlyList<ScsPlateRegion> Ets2Regions = Array.AsReadOnly(
        new ScsPlateRegion[]
        {
        new("Albanien", "albania"),
        new("Belgien", "belgium"),
        new("Bosnien und Herzegowina", "bosnia"),
        new("Bulgarien", "bulgaria"),
        new("Dänemark", "denmark"),
        new("Deutschland", "germany"),
        new("Estland", "estonia"),
        new("Finnland", "finland"),
        new("Frankreich", "france"),
        new("Griechenland", "greece"),
        new("Großbritannien", "uk"),
        new("Irland", "ireland"),
        new("Italien", "italy"),
        new("Kosovo", "kosovo"),
        new("Kroatien", "croatia"),
        new("Lettland", "latvia"),
        new("Litauen", "lithuania"),
        new("Luxemburg", "luxembourg"),
        new("Montenegro", "montenegro"),
        new("Niederlande", "netherlands"),
        new("Norwegen", "norway"),
        new("Österreich", "austria"),
        new("Polen", "poland"),
        new("Portugal", "portugal"),
        new("Rumänien", "romania"),
        new("Russland", "russia"),
        new("Schweden", "sweden"),
        new("Schweiz", "switzerland"),
        new("Serbien", "serbia"),
        new("Slowakei", "slovakia"),
        new("Slowenien", "slovenia"),
        new("Spanien", "spain"),
        new("Türkei", "turkey"),
        new("Tschechien", "czech"),
        new("Ukraine", "ukraine"),
        new("Ungarn", "hungary"),
            new("Eigene Farben (Custom-Kennzeichen)", "custom"),
            new("Anderer Ländercode / Map-Mod …", OtherCode),
        });

    private static readonly IReadOnlyList<ScsPlateRegion> AtsRegions = Array.AsReadOnly(
        new ScsPlateRegion[]
        {
        new("Arizona", "arizona"),
        new("Arkansas", "arkansas"),
        new("Colorado", "colorado"),
        new("Idaho", "idaho"),
        new("Illinois", "illinois"),
        new("Iowa", "iowa"),
        new("Kalifornien", "california"),
        new("Kansas", "kansas"),
        new("Louisiana", "louisiana"),
        new("Missouri", "missouri"),
        new("Montana", "montana"),
        new("Nebraska", "nebraska"),
        new("Nevada", "nevada"),
        new("New Mexico", "new_mexico"),
        new("Oklahoma", "oklahoma"),
        new("Oregon", "oregon"),
        new("South Dakota", "south_dakota"),
        new("Texas", "texas"),
        new("Utah", "utah"),
        new("Washington", "washington"),
        new("Wyoming", "wyoming"),
            new("Eigene Farben (Custom-Kennzeichen)", "custom"),
            new("Anderer Bundesstaat / Map-Mod …", OtherCode),
        });

    public static IReadOnlyList<ScsPlateRegion> For(GameType game) =>
        game switch
        {
            GameType.Ets2 => Ets2Regions,
            GameType.Ats => AtsRegions,
            _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Unbekanntes Spiel.")
        };

    public static ScsPlateRegion DefaultFor(GameType game)
    {
        var code = game == GameType.Ets2 ? "germany" : "california";
        return For(game).Single(region => region.Code == code);
    }
}
