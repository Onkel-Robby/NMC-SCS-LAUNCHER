using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

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
                Ets2ProfileHomePath = @"D:\SCS-Profiles\ETS2",
                AtsProfileHomePath = @"D:\SCS-Profiles\ATS",
                ShowStartCheck = false
            };

            await store.SaveAsync(expected);
            var actual = await store.LoadAsync();

            Assert.Equal(expected.Ets2InstallPath, actual.Ets2InstallPath);
            Assert.Equal(expected.Ets2ProfileHomePath, actual.Ets2ProfileHomePath);
            Assert.Equal(expected.AtsProfileHomePath, actual.AtsProfileHomePath);
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
    public async Task ConcurrentSaves_ProduceValidSettingsWithoutSharedTmpCollisions()
    {
        var root = Path.Combine(Path.GetTempPath(), "nmc-settings-parallel", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        try
        {
            var store = new JsonSettingsStore(file);
            var writes = Enumerable.Range(0, 32)
                .Select(i => store.SaveAsync(new LauncherSettings
                {
                    Ets2InstallPath = $"D:/Test/Game-{i}",
                    AtsInstallPath = $"E:/Test/Game-{i}",
                    ShowStartCheck = i % 2 == 0
                }));
            await Task.WhenAll(writes);

            var loaded = await store.LoadAsync();
            Assert.NotNull(loaded.Ets2InstallPath);
            Assert.StartsWith("D:/Test/Game-", loaded.Ets2InstallPath);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentInstances_DoNotReuseTheSameTemporaryFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "nmc-settings-parallel", Guid.NewGuid().ToString("N"));
        var file = Path.Combine(root, "settings.json");
        try
        {
            var stores = Enumerable.Range(0, 8).Select(_ => new JsonSettingsStore(file)).ToArray();
            await Task.WhenAll(stores.Select((store, index) =>
                store.SaveAsync(new LauncherSettings { Ets2InstallPath = $"X:/Steam/Game-{index}" })));

            var loaded = await stores[0].LoadAsync();
            Assert.StartsWith("X:/Steam/Game-", loaded.Ets2InstallPath);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AppDataRoot_UsesNmcProductFolder()
    {
        Assert.EndsWith(Path.Combine("NMC Network", "NMC SCS Launcher"), AppPaths.AppDataRoot);
    }
}
