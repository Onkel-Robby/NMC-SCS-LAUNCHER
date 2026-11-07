using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Core.Tests;

public sealed class ScsPlateRegionCatalogTests
{
    [Theory]
    [InlineData(GameType.Ets2, 36)]
    [InlineData(GameType.Ats, 21)]
    public void CatalogHasAllNativeRegionsAndTwoExplicitSpecialChoices(GameType game, int count)
    {
        var regions = ScsPlateRegionCatalog.For(game);
        Assert.Equal(count + 2, regions.Count);
        Assert.Equal(count, regions.Count(region => region.Code is not "custom" and not ScsPlateRegionCatalog.OtherCode));
        Assert.All(regions.Where(region => region.Code != ScsPlateRegionCatalog.OtherCode),
            region => Assert.Matches("^[a-z][a-z0-9_]{1,31}$", region.Code));
    }

    [Fact]
    public void Ets2ListsCountriesAndDefaultsToGermany()
    {
        var regions = ScsPlateRegionCatalog.For(GameType.Ets2);

        Assert.Equal("germany", ScsPlateRegionCatalog.DefaultFor(GameType.Ets2).Code);
        Assert.Contains(regions, region => region is { Name: "Deutschland", Code: "germany" });
        Assert.Contains(regions, region => region is { Name: "Großbritannien", Code: "uk" });
        Assert.DoesNotContain(regions, region => region.Code == "california");
        Assert.Equal(regions.Count, regions.Select(region => region.Code).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AtsListsUsStatesAndDefaultsToCalifornia()
    {
        var regions = ScsPlateRegionCatalog.For(GameType.Ats);

        Assert.Equal("california", ScsPlateRegionCatalog.DefaultFor(GameType.Ats).Code);
        Assert.Contains(regions, region => region is { Name: "Kalifornien", Code: "california" });
        Assert.Contains(regions, region => region is { Name: "Texas", Code: "texas" });
        Assert.DoesNotContain(regions, region => region.Code == "germany");
        Assert.Equal(regions.Count, regions.Select(region => region.Code).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(GameType.Ets2)]
    [InlineData(GameType.Ats)]
    public void SupportsExistingCustomPlatesAndUnlistedMapMods(GameType game)
    {
        var regions = ScsPlateRegionCatalog.For(game);
        Assert.Contains(regions, region => region.Code == "custom");
        Assert.Contains(regions, region => region.Code == ScsPlateRegionCatalog.OtherCode);
        Assert.Contains("Custom", regions.Single(region => region.Code == "custom").DisplayName);
        Assert.Equal("Anderer", regions.Single(region => region.Code == ScsPlateRegionCatalog.OtherCode).DisplayName[..7]);
    }
}
