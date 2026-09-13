namespace NmcScsLauncher.Core;

public enum GameInstallationSource
{
    SavedPath,
    SteamAutoDetection,
    ManualSelection
}

public sealed record GameInstallation(
    GameType GameType,
    string InstallPath,
    string ExecutablePath,
    GameInstallationSource Source);

public interface IGameInstallationDetector
{
    GameInstallation? Validate(GameType gameType, string installPath, GameInstallationSource source = GameInstallationSource.ManualSelection);

    Task<GameInstallation?> DetectAsync(
        GameType gameType,
        string? preferredInstallPath = null,
        CancellationToken cancellationToken = default);
}
