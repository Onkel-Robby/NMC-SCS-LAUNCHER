using System.Text;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsProfileSaveLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NmcScsLauncher-ProfileLocatorTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FindsLocalAndSteamProfilesAndTheirSaves()
    {
        Directory.CreateDirectory(_root);

        var localName = Convert.ToHexString(Encoding.UTF8.GetBytes("Robby"));
        var localProfile = Path.Combine(_root, "profiles", localName);
        var localSave = Path.Combine(localProfile, "save", "1");
        Directory.CreateDirectory(localSave);
        await File.WriteAllTextAsync(
            Path.Combine(localProfile, "profile.sii"),
            "SiiNunit\n{\n profile_name: \"Robby\"\n}\n");
        await File.WriteAllTextAsync(
            Path.Combine(localSave, "game.sii"),
            "SiiNunit\n{\n money_account: 1\n}\n");

        var steamName = Convert.ToHexString(Encoding.UTF8.GetBytes("Steam Robby"));
        var steamProfile = Path.Combine(_root, "steam_profiles", steamName);
        var steamSave = Path.Combine(steamProfile, "save", "autosave");
        Directory.CreateDirectory(steamSave);
        await File.WriteAllTextAsync(
            Path.Combine(steamProfile, "profile.sii"),
            "SiiNunit\n{\n profile_name: \"Steam Robby\"\n}\n");
        await File.WriteAllTextAsync(
            Path.Combine(steamSave, "game.sii"),
            "SiiNunit\n{\n money_account: 2\n}\n");

        var locator = new ScsProfileSaveLocator();
        var profiles = await locator.FindProfilesAsync(GameType.Ets2, _root);

        Assert.Equal(2, profiles.Count);
        Assert.Contains(profiles, profile =>
            profile.StorageKind == ScsProfileStorageKind.Local &&
            profile.DisplayName == "Robby");
        Assert.Contains(profiles, profile =>
            profile.StorageKind == ScsProfileStorageKind.SteamCloud &&
            profile.DisplayName == "Steam Robby");

        var local = profiles.Single(profile => profile.StorageKind == ScsProfileStorageKind.Local);
        var saves = await locator.FindSavesAsync(local);

        var save = Assert.Single(saves);
        Assert.Equal(ScsSaveKind.Manual, save.Kind);
        Assert.Equal("Save 1", save.DisplayName);
        Assert.Equal(Path.GetFullPath(Path.Combine(localSave, "game.sii")), save.GameSiiPath);
    }

    [Fact]
    public async Task FindsActualCloudSaveInSteamUserdataAndKeepsGamesSeparate()
    {
        Directory.CreateDirectory(_root);
        var steamRoot = Path.Combine(_root, "Steam");
        var profileId = Convert.ToHexString(Encoding.UTF8.GetBytes("Cloud Driver"));
        var etsRemote = Path.Combine(steamRoot, "userdata", "123456", "227300", "remote");
        var atsRemote = Path.Combine(steamRoot, "userdata", "123456", "270880", "remote");

        var etsProfile = Path.Combine(etsRemote, "profiles", profileId);
        var etsSave = Path.Combine(etsProfile, "save", "quicksave");
        Directory.CreateDirectory(etsSave);
        await File.WriteAllTextAsync(Path.Combine(etsProfile, "profile.sii"), "SiiNunit");
        await File.WriteAllTextAsync(Path.Combine(etsSave, "game.sii"), "SiiNunit");

        var atsProfile = Path.Combine(atsRemote, "profiles", profileId);
        var atsSave = Path.Combine(atsProfile, "save", "autosave");
        Directory.CreateDirectory(atsSave);
        await File.WriteAllTextAsync(Path.Combine(atsProfile, "profile.sii"), "SiiNunit");
        await File.WriteAllTextAsync(Path.Combine(atsSave, "game.sii"), "SiiNunit");

        // Some directory entries under userdata are not Steam accounts.
        var invalidAccount = Path.Combine(steamRoot, "userdata", "backup", "227300",
            "remote", "profiles", "FakeProfile");
        Directory.CreateDirectory(invalidAccount);
        await File.WriteAllTextAsync(Path.Combine(invalidAccount, "profile.sii"), "SiiNunit");

        var locator = new ScsProfileSaveLocator(new SteamLibraryLocator([steamRoot]));
        var ets = Assert.Single(await locator.FindSteamCloudProfilesAsync(GameType.Ets2));
        var ats = Assert.Single(await locator.FindSteamCloudProfilesAsync(GameType.Ats));

        Assert.Equal(ScsProfileStorageKind.SteamCloudRemote, ets.StorageKind);
        Assert.Equal(ScsProfileStorageKind.SteamCloudRemote, ats.StorageKind);
        Assert.Equal(Path.GetFullPath(etsProfile), ets.ProfileDirectory);
        Assert.Equal(Path.GetFullPath(atsProfile), ats.ProfileDirectory);
        Assert.Contains("Steam Cloud", ets.DisplayName);
        Assert.Equal(ScsSaveKind.QuickSave, Assert.Single(await locator.FindSavesAsync(ets)).Kind);
        Assert.Equal(ScsSaveKind.AutoSave, Assert.Single(await locator.FindSavesAsync(ats)).Kind);
    }

    [Fact]
    public async Task CloudProfileWithoutProfileSiiIsIgnored()
    {
        Directory.CreateDirectory(_root);
        var steamRoot = Path.Combine(_root, "Steam");
        Directory.CreateDirectory(Path.Combine(steamRoot, "userdata", "789", "227300",
            "remote", "profiles", "AABBCCDD"));

        var locator = new ScsProfileSaveLocator(new SteamLibraryLocator([steamRoot]));
        Assert.Empty(await locator.FindSteamCloudProfilesAsync(GameType.Ets2));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }
}
