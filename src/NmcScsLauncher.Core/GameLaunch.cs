namespace NmcScsLauncher.Core;

public enum LaunchCheckSeverity
{
    Success,
    Warning,
    Error
}

public sealed record LaunchCheckItem(
    string Title,
    string Message,
    LaunchCheckSeverity Severity);

public sealed record GameLaunchPlan(
    Modset Modset,
    GameInstallation Installation,
    string GameDataDirectory,
    IReadOnlyList<string> AdditionalArguments,
    IReadOnlyList<LaunchCheckItem> Checks)
{
    public bool CanLaunch => Checks.All(static check => check.Severity != LaunchCheckSeverity.Error);

    public bool HasWarnings => Checks.Any(static check => check.Severity == LaunchCheckSeverity.Warning);
}

public interface IGameLaunchService
{
    Task<GameLaunchPlan> PrepareAsync(
        Modset modset,
        GameInstallation installation,
        CancellationToken cancellationToken = default);

    Task<int> LaunchAsync(GameLaunchPlan plan, CancellationToken cancellationToken = default);
}
