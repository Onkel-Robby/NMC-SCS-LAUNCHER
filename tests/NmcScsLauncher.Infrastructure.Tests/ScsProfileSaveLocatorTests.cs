using System.Text;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsProfileSaveLocatorTests
{
    [Fact]
    public async Task FindsLocalAndSteamProfilesAndTheirSaveDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        try
        {
            var localName = Convert.ToHexString(Encoding.UTF8.GetBytes("Robby"));
            var localProfile = Path.Combine(root, "profiles", localName);
            var steamProfile = Path.Combine(root, "steam_profiles", "76561198000000000");
            Directory.CreateDirectory(localProfile);
            Directory.CreateDirectory(steamProfile);
            await File.WriteAllTextAsync(Path.Combine(localProfile, "profile.sii"), "profile");
            await File.WriteAllTextAsync(Path.Combine(steamProfile, "profile.sii"), "profile");

            var autosave = Path.Combine(localProfile, "save", "autosave");
            var manual = Path.Combine(localProfile, "save", "7");
            Directory.CreateDirectory(autosave);
            Directory.CreateDirectory(manual);
            await File.WriteAllTextAsync(Path.Combine(autosave, "game.sii"), "SiiNunit\n{}");
            await File.WriteAllTextAsync(Path.Combine(manual, "game.sii"), "SiiNunit\n{}");

            var locator = new ScsProfileSaveLocator();
            var profiles = await locator.FindProfilesAsync(GameType.Ets2, root);

            Assert.Equal(2, profiles.Count);
            var local = Assert.Single(profiles.Where(profile => profile.StorageKind == ScsProfileStorageKind.Local));
            Assert.Equal("Robby", local.DisplayName);
            Assert.Equal(Path.GetFullPath(localProfile), local.ProfileDirectory);

            var steam = Assert.Single(profiles.Where(profile => profile.StorageKind == ScsProfileStorageKind.SteamCloud));
            Assert.Equal("76561198000000000", steam.DisplayName);

            var saves = await locator.FindSavesAsync(local);
            Assert.Equal(2, saves.Count);
            Assert.Contains(saves, save => save.Kind == ScsSaveKind.AutoSave && save.DisplayName == "Autosave");
            Assert.Contains(saves, save => save.Kind == ScsSaveKind.Manual && save.DisplayName == "Save 7");
            Assert.All(saves, save => Assert.Equal("game.sii", Path.GetFileName(save.GameSiiPath)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task IgnoresProfilesAndSavesWithoutRequiredSiiFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "profiles", "missing-profile-sii"));

            var validProfile = Path.Combine(root, "profiles", "valid");
            Directory.CreateDirectory(validProfile);
            await File.WriteAllTextAsync(Path.Combine(validProfile, "profile.sii"), "profile");
            Directory.CreateDirectory(Path.Combine(validProfile, "save", "empty"));

            var locator = new ScsProfileSaveLocator();
            var profiles = await locator.FindProfilesAsync(GameType.Ats, root);
            var profile = Assert.Single(profiles);
            var saves = await locator.FindSavesAsync(profile);

            Assert.Empty(saves);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
