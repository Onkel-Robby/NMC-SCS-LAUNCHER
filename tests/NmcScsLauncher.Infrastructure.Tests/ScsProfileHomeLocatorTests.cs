using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsProfileHomeLocatorTests
{
    [Fact]
    public void PrefersCustomHomeAndDeduplicatesRedirectedDocuments()
    {
        var root = Path.Combine(Path.GetTempPath(), "scs-home-locator", Guid.NewGuid().ToString("N"));
        var documents = Path.Combine(root, "OneDrive", "Documents");
        var custom = Path.Combine(root, "SCS", "Euro Truck Simulator 2");
        var found = ScsProfileHomeLocator.GetCandidates(
            GameType.Ets2,
            custom,
            [documents, documents]);

        Assert.Equal(2, found.Count);
        Assert.Equal(Path.GetFullPath(custom), found[0]);
        Assert.Equal(Path.Combine(documents, "Euro Truck Simulator 2"), found[1]);
    }

    [Fact]
    public void AtsAndEts2ResolveDistinctDocumentGameDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "scs-home-locator", Guid.NewGuid().ToString("N"));
        var ats = Assert.Single(ScsProfileHomeLocator.GetCandidates(GameType.Ats, null, [root]));
        var ets = Assert.Single(ScsProfileHomeLocator.GetCandidates(GameType.Ets2, null, [root]));

        Assert.EndsWith("American Truck Simulator", ats);
        Assert.EndsWith("Euro Truck Simulator 2", ets);
    }

    [Theory]
    [InlineData("profiles")]
    [InlineData("steam_profiles")]
    [InlineData("Profiles")]
    public void SelectedProfileContainerNormalizesToGameHome(string folderName)
    {
        var root = Path.Combine(Path.GetTempPath(), "scs-home-locator", Guid.NewGuid().ToString("N"), "American Truck Simulator");
        Assert.Equal(root, ScsProfileHomeLocator.NormalizeSelectedHome(Path.Combine(root, folderName)));
    }

    [Fact]
    public void SelectedGameHomeIsKept()
    {
        var root = Path.Combine(Path.GetTempPath(), "scs-home-locator", Guid.NewGuid().ToString("N"), "American Truck Simulator");
        Assert.Equal(root, ScsProfileHomeLocator.NormalizeSelectedHome(root));
    }
}
