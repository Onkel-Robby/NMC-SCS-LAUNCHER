namespace NmcScsLauncher.Core;

public sealed class LauncherSettings
{
    public string? Ets2InstallPath { get; set; }

    public string? AtsInstallPath { get; set; }

    public string? DefaultModsetRoot { get; set; }

    public Guid? LastSelectedModsetId { get; set; }

    public bool ShowStartCheck { get; set; } = true;

    public bool ShowModCount { get; set; } = true;

    public bool RememberLastModset { get; set; } = true;
}

public interface ISettingsStore
{
    Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken = default);
}

public interface IAppLogger
{
    Task WriteAsync(string level, string message, Exception? exception = null, CancellationToken cancellationToken = default);
}
