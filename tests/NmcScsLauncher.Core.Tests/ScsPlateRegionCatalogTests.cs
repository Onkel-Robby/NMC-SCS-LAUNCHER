using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Core.Tests;

public sealed class ScsPlateRegionCatalogTests
{
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
