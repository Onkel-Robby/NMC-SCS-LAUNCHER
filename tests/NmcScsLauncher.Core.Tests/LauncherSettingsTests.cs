using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Core.Tests;

public sealed class LauncherSettingsTests
{
    [Fact]
    public void Defaults_AreSafeForFirstStart()
    {
        var settings = new LauncherSettings();

        Assert.True(settings.ShowStartCheck);
        Assert.True(settings.ShowModCount);
        Assert.True(settings.RememberLastModset);
        Assert.Null(settings.Ets2InstallPath);
        Assert.Null(settings.AtsInstallPath);
    }
}
