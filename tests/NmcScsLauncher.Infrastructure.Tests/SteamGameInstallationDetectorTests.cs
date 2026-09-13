using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class SteamGameInstallationDetectorTests
{
    [Fact]
    public async Task DetectsEts2FromSteamManifestAndX64Executable()
    {
        var steamRoot = CreateTemporaryDirectory();
        try
        {
            var steamApps = Directory.CreateDirectory(Path.Combine(steamRoot, "steamapps"));
            File.WriteAllText(
                Path.Combine(steamApps.FullName, "appmanifest_227300.acf"),
                "\"AppState\"\n{\n    \"appid\" \"227300\"\n    \"installdir\" \"Euro Truck Simulator 2\"\n}");

            var gameRoot = Path.Combine(steamApps.FullName, "common", "Euro Truck Simulator 2");
            var executable = Path.Combine(gameRoot, "bin", "win_x64", "eurotrucks2.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(executable, string.Empty);

            var detector = new SteamGameInstallationDetector(new SteamLibraryLocator([steamRoot]));
            var installation = await detector.DetectAsync(GameType.Ets2);

            Assert.NotNull(installation);
            Assert.Equal(GameInstallationSource.SteamAutoDetection, installation.Source);
            Assert.Equal(Path.GetFullPath(gameRoot), installation.InstallPath);
            Assert.Equal(Path.GetFullPath(executable), installation.ExecutablePath);
        }
        finally
        {
            Directory.Delete(steamRoot, recursive: true);
        }
    }

    [Fact]
    public async Task PreferredValidPathWinsOverSteamScanning()
    {
        var gameRoot = CreateTemporaryDirectory();
        try
        {
            var executable = Path.Combine(gameRoot, "bin", "win_x64", "amtrucks.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(executable, string.Empty);

            var detector = new SteamGameInstallationDetector(new SteamLibraryLocator(Array.Empty<string>()));
            var installation = await detector.DetectAsync(GameType.Ats, gameRoot);

            Assert.NotNull(installation);
            Assert.Equal(GameInstallationSource.SavedPath, installation.Source);
            Assert.Equal(Path.GetFullPath(gameRoot), installation.InstallPath);
        }
        finally
        {
            Directory.Delete(gameRoot, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "NmcScsLauncherTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
