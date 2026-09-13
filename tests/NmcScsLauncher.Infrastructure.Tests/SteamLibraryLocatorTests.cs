using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class SteamLibraryLocatorTests
{
    [Fact]
    public void FindsPrimaryAndConfiguredSteamLibrary()
    {
        var root = CreateTemporaryDirectory();
        var secondLibrary = CreateTemporaryDirectory();
        try
        {
            var steamApps = Directory.CreateDirectory(Path.Combine(root, "steamapps"));
            var encodedSecondLibrary = secondLibrary.Replace("\\", "\\\\");
            File.WriteAllText(
                Path.Combine(steamApps.FullName, "libraryfolders.vdf"),
                $"\"libraryfolders\"\n{{\n    \"1\"\n    {{\n        \"path\"    \"{encodedSecondLibrary}\"\n    }}\n}}");

            var locator = new SteamLibraryLocator([root]);
            var libraries = locator.FindLibraryRoots();

            Assert.Contains(Path.GetFullPath(root), libraries, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.GetFullPath(secondLibrary), libraries, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(secondLibrary, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
