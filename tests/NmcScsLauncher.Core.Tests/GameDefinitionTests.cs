using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.Core.Tests;

public sealed class GameDefinitionTests
{
    [Fact]
    public void Ets2DefinitionUsesExpectedSteamIdentityAndExecutable()
    {
        var definition = GameDefinition.For(GameType.Ets2);

        Assert.Equal(227300, definition.SteamAppId);
        Assert.Equal("Euro Truck Simulator 2", definition.DefaultInstallDirectoryName);
        Assert.EndsWith(Path.Combine("bin", "win_x64", "eurotrucks2.exe"), definition.ExecutableRelativePath);
    }

    [Fact]
    public void AtsDefinitionUsesExpectedSteamIdentityAndExecutable()
    {
        var definition = GameDefinition.For(GameType.Ats);

        Assert.Equal(270880, definition.SteamAppId);
        Assert.Equal("American Truck Simulator", definition.DefaultInstallDirectoryName);
        Assert.EndsWith(Path.Combine("bin", "win_x64", "amtrucks.exe"), definition.ExecutableRelativePath);
    }
}
