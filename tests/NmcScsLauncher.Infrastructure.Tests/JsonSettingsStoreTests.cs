using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "nmc-scs-launcher-tests", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");

        try
        {
            var store = new JsonSettingsStore(file);
            var expected = new LauncherSettings
            {
                Ets2InstallPath = @"D:\Games\ETS2",
                ShowStartCheck = false
            };

            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();

            Assert.Equal(expected.Ets2InstallPath, actual.Ets2InstallPath);
            Assert.False(actual.ShowStartCheck);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AppDataRoot_UsesNmcProductFolder()
    {
        Assert.EndsWith(Path.Combine("NMC Network", "NMC SCS Launcher"), AppPaths.AppDataRoot);
    }
}
