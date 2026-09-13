namespace NmcScsLauncher.Infrastructure;

public static class AppPaths
{
    public static string AppDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NMC Network",
        "NMC SCS Launcher");

    public static string SettingsFile => Path.Combine(AppDataRoot, "settings.json");

    public static string ModsetsFile => Path.Combine(AppDataRoot, "modsets.json");

    public static string LogsDirectory => Path.Combine(AppDataRoot, "logs");

    public static string CacheDirectory => Path.Combine(AppDataRoot, "cache");
}
