namespace NmcScsLauncher.Core;

public sealed record GameDefinition(
    GameType GameType,
    string DisplayName,
    int SteamAppId,
    string DefaultInstallDirectoryName,
    string ExecutableRelativePath,
    string HomeDirectoryName,
    string ProcessName)
{
    public static GameDefinition For(GameType gameType) => gameType switch
    {
        GameType.Ets2 => new(
            GameType.Ets2,
            "Euro Truck Simulator 2",
            227300,
            "Euro Truck Simulator 2",
            Path.Combine("bin", "win_x64", "eurotrucks2.exe"),
            "Euro Truck Simulator 2",
            "eurotrucks2"),
        GameType.Ats => new(
            GameType.Ats,
            "American Truck Simulator",
            270880,
            "American Truck Simulator",
            Path.Combine("bin", "win_x64", "amtrucks.exe"),
            "American Truck Simulator",
            "amtrucks"),
        _ => throw new ArgumentOutOfRangeException(nameof(gameType), gameType, null)
    };
}
