using System.Reflection;

namespace NmcScsLauncher.App.Services;

public static class AppVersionInfo
{
    public static string Current { get; } = ResolveVersion();

    public static string Display => $"Version {Current}";

    private static string ResolveVersion()
    {
        var assembly = typeof(AppVersionInfo).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plusIndex = informational.IndexOf('+');
            return plusIndex >= 0 ? informational[..plusIndex] : informational;
        }

        var version = assembly.GetName().Version;
        return version is null ? "unknown" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
