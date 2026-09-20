using System.Diagnostics;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public static class ScsModsetPathResolver
{
    public static bool UsesDirectModDirectory(Modset modset) =>
        !string.IsNullOrWhiteSpace(modset.ModDirectoryPath);

    public static string GetModDirectory(Modset modset)
    {
        ArgumentNullException.ThrowIfNull(modset);
        return UsesDirectModDirectory(modset)
            ? Path.GetFullPath(modset.ModDirectoryPath!)
            : Path.Combine(
                Path.GetFullPath(modset.HomeBasePath),
                GameDefinition.For(modset.Game).HomeDirectoryName,
                "mod");
    }

    public static string GetRuntimeGameDataDirectory(Modset modset)
    {
        ArgumentNullException.ThrowIfNull(modset);
        return Path.Combine(
            Path.GetFullPath(modset.HomeBasePath),
            GameDefinition.For(modset.Game).HomeDirectoryName);
    }

    public static string GetStandardGameDataDirectory(GameType gameType)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            GameDefinition.For(gameType).HomeDirectoryName);
    }

    public static string GetLocalProfilesDirectory(Modset modset) =>
        UsesDirectModDirectory(modset)
            ? Path.Combine(GetStandardGameDataDirectory(modset.Game), "profiles")
            : Path.Combine(GetRuntimeGameDataDirectory(modset), "profiles");

    public static string GetSteamProfilesDirectory(Modset modset) =>
        UsesDirectModDirectory(modset)
            ? Path.Combine(GetStandardGameDataDirectory(modset.Game), "steam_profiles")
            : Path.Combine(GetRuntimeGameDataDirectory(modset), "steam_profiles");

    public static void EnsureRuntimeWorkspace(Modset modset)
    {
        ArgumentNullException.ThrowIfNull(modset);
        if (!UsesDirectModDirectory(modset))
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Direkte Mod-Ordner werden derzeit nur unter Windows unterstützt.");
        }

        var modDirectory = GetModDirectory(modset);
        if (!Directory.Exists(modDirectory))
        {
            throw new DirectoryNotFoundException($"Der Mod-Ordner wurde nicht gefunden: {modDirectory}");
        }

        var runtimeGameData = GetRuntimeGameDataDirectory(modset);
        Directory.CreateDirectory(runtimeGameData);

        var standardGameData = GetStandardGameDataDirectory(modset.Game);
        var localProfiles = Path.Combine(standardGameData, "profiles");
        var steamProfiles = Path.Combine(standardGameData, "steam_profiles");
        Directory.CreateDirectory(localProfiles);
        Directory.CreateDirectory(steamProfiles);

        EnsureDirectoryJunction(Path.Combine(runtimeGameData, "mod"), modDirectory);
        EnsureDirectoryJunction(Path.Combine(runtimeGameData, "profiles"), localProfiles);
        EnsureDirectoryJunction(Path.Combine(runtimeGameData, "steam_profiles"), steamProfiles);
    }

    private static void EnsureDirectoryJunction(string linkPath, string targetPath)
    {
        linkPath = Path.GetFullPath(linkPath);
        targetPath = Path.GetFullPath(targetPath);

        if (Directory.Exists(linkPath))
        {
            var attributes = File.GetAttributes(linkPath);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                var resolved = new DirectoryInfo(linkPath).ResolveLinkTarget(returnFinalTarget: false);
                if (resolved is not null
                    && string.Equals(
                        Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolved.FullName)),
                        Path.TrimEndingDirectorySeparator(targetPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Directory.Delete(linkPath);
            }
            else
            {
                if (Directory.EnumerateFileSystemEntries(linkPath).Any())
                {
                    throw new IOException(
                        $"Der interne Laufzeitordner '{linkPath}' enthält bereits echte Dateien und wird aus Sicherheitsgründen nicht ersetzt.");
                }

                Directory.Delete(linkPath);
            }
        }
        else if (File.Exists(linkPath))
        {
            throw new IOException($"Der interne Laufzeitpfad kollidiert mit einer Datei: {linkPath}");
        }

        var parent = Path.GetDirectoryName(linkPath)
            ?? throw new IOException("Der interne Junction-Pfad ist ungültig.");
        Directory.CreateDirectory(parent);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList =
            {
                "/d",
                "/c",
                "mklink",
                "/J",
                linkPath,
                targetPath
            }
        }) ?? throw new IOException("Der Windows-Junction-Prozess konnte nicht gestartet werden.");

        process.WaitForExit();
        if (process.ExitCode != 0 || !Directory.Exists(linkPath))
        {
            var error = process.StandardError.ReadToEnd().Trim();
            if (string.IsNullOrWhiteSpace(error))
            {
                error = process.StandardOutput.ReadToEnd().Trim();
            }

            throw new IOException(
                string.IsNullOrWhiteSpace(error)
                    ? $"Der interne Verzeichnislink '{linkPath}' konnte nicht erstellt werden."
                    : $"Der interne Verzeichnislink konnte nicht erstellt werden: {error}");
        }
    }
}
