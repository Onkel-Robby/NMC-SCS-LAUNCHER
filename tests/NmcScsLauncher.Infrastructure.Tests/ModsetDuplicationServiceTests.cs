using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ModsetDuplicationServiceTests
{
    [Fact]
    public async Task DuplicateRejectsTargetInsideSourceTree()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var manager = new ModsetManager(new JsonModsetStore(Path.Combine(root, "modsets.json")));
        var sourceHome = Path.Combine(root, "source");
        var source = await manager.CreateAsync(new ModsetDraft(GameType.Ats, "Source ATS", null, sourceHome));
        var service = new ModsetDuplicationService(manager);

        await Assert.ThrowsAsync<ModsetValidationException>(() => service.DuplicateAsync(new ModsetDuplicationRequest(
            source.Id,
            "Nested",
            Path.Combine(sourceHome, "nested-copy"),
            ModsetCopyContent.Mods)));

        Assert.Single(await manager.GetAllAsync());
    }
}
