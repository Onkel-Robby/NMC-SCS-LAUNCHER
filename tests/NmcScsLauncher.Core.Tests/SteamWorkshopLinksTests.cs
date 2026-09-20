using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Core.Tests;

public sealed class SteamWorkshopLinksTests
{
    [Fact]
    public void GetItemUriBuildsCanonicalWorkshopPage()
    {
        var uri = SteamWorkshopLinks.GetItemUri(1234567890UL);

        Assert.Equal(
            "https://steamcommunity.com/sharedfiles/filedetails/?id=1234567890",
            uri.AbsoluteUri);
    }

    [Fact]
    public void GetItemUriRejectsZeroId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SteamWorkshopLinks.GetItemUri(0));
    }
}
