using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class JsonModsetStoreTests
{
    [Fact]
    public async Task RoundTripsModsets()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var file = Path.Combine(root, "modsets.json");
            var store = new JsonModsetStore(file);
            var expected = new Modset
            {
                Id = Guid.NewGuid(),
                Game = GameType.Ets2,
                Name = "Map Combo",
                Description = "Test",
                HomeBasePath = Path.Combine(root, "home"),
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
                IsManagedDirectory = true
            };

            await store.SaveAsync([expected]);
            var loaded = await store.LoadAsync();

            var actual = Assert.Single(loaded);
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Game, actual.Game);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.HomeBasePath, actual.HomeBasePath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
