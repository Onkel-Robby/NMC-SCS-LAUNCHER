using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Core.Tests;

public sealed class WorkshopContentItemTests
{
    [Fact]
    public void DisplayNameUsesSteamTitleWhenAvailable()
    {
        var item = new WorkshopContentItem(
            GameType.Ets2,
            1234,
            @"C:\Workshop\1234",
            @"C:\Steam",
            DateTimeOffset.UnixEpoch,
            "  Real Mod Name  ");

        Assert.True(item.HasTitle);
        Assert.Equal("Real Mod Name", item.DisplayName);
    }

    [Fact]
    public void DisplayNameFallsBackToPublishedFileId()
    {
        var item = new WorkshopContentItem(
            GameType.Ets2,
            9876,
            @"C:\Workshop\9876",
            @"C:\Steam",
            DateTimeOffset.UnixEpoch);

        Assert.False(item.HasTitle);
        Assert.Equal("Workshop 9876", item.DisplayName);
        Assert.Equal(DateTimeOffset.UnixEpoch, item.DisplayLastUpdatedUtc);
    }
}
