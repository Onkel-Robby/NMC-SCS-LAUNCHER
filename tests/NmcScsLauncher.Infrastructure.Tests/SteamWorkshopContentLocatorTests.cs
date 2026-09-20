using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class SteamWorkshopContentLocatorTests
{
    [Fact]
    public async Task ScanFindsOnlyNumericItemsForRequestedGame()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var ets2Root = Path.Combine(root, "steamapps", "workshop", "content", GameDefinition.For(GameType.Ets2).SteamAppId.ToString());
        Directory.CreateDirectory(Path.Combine(ets2Root, "1234567890"));
        Directory.CreateDirectory(Path.Combine(ets2Root, "not-an-item"));

        var atsRoot = Path.Combine(root, "steamapps", "workshop", "content", GameDefinition.For(GameType.Ats).SteamAppId.ToString());
        Directory.CreateDirectory(Path.Combine(atsRoot, "9876543210"));

        var locator = new SteamWorkshopContentLocator(new SteamLibraryLocator(new[] { root }));
        var ets2 = await locator.ScanAsync(GameType.Ets2);
        var ats = await locator.ScanAsync(GameType.Ats);

        var ets2Item = Assert.Single(ets2.Items);
        Assert.Equal(1234567890UL, ets2Item.PublishedFileId);
        Assert.Equal(GameType.Ets2, ets2Item.Game);
        Assert.Equal(Path.Combine(ets2Root, "1234567890"), ets2Item.ContentPath);
        Assert.Single(ets2.ScannedContentRoots);

        var atsItem = Assert.Single(ats.Items);
        Assert.Equal(9876543210UL, atsItem.PublishedFileId);
        Assert.Equal(GameType.Ats, atsItem.Game);
    }

    [Fact]
    public async Task ScanUsesSteamLibraryInferredFromGameInstallPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var installPath = Path.Combine(
            root,
            "steamapps",
            "common",
            GameDefinition.For(GameType.Ets2).DefaultInstallDirectoryName);
        Directory.CreateDirectory(installPath);

        var workshopRoot = Path.Combine(
            root,
            "steamapps",
            "workshop",
            "content",
            GameDefinition.For(GameType.Ets2).SteamAppId.ToString());
        var workshopItemPath = Path.Combine(workshopRoot, "2468013579");
        Directory.CreateDirectory(workshopItemPath);

        var locator = new SteamWorkshopContentLocator(new SteamLibraryLocator(Array.Empty<string>()));
        var snapshot = await locator.ScanAsync(GameType.Ets2, installPath);

        var item = Assert.Single(snapshot.Items);
        Assert.Equal(2468013579UL, item.PublishedFileId);
        Assert.Equal(Path.GetFullPath(root), item.SteamLibraryRoot);
        Assert.Equal(Path.GetFullPath(workshopItemPath), item.ContentPath);
        Assert.Equal(Path.GetFullPath(workshopRoot), Assert.Single(snapshot.ScannedContentRoots));
    }

    [Fact]
    public async Task ScanReturnsEmptySnapshotWhenWorkshopRootDoesNotExist()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var locator = new SteamWorkshopContentLocator(new SteamLibraryLocator(new[] { root }));

        var snapshot = await locator.ScanAsync(GameType.Ets2);

        Assert.Empty(snapshot.Items);
        Assert.Empty(snapshot.ScannedContentRoots);
        Assert.Empty(snapshot.Warnings);
    }
}
