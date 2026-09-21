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
