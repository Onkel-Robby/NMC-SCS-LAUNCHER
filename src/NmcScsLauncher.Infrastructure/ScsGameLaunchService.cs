using System.Diagnostics;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsGameLaunchService : IGameLaunchService
{
    public Task<GameLaunchPlan> PrepareAsync(
        Modset modset,
        GameInstallation installation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(modset);
        ArgumentNullException.ThrowIfNull(installation);
        cancellationToken.ThrowIfCancellationRequested();

        var checks = new List<LaunchCheckItem>();
        var definition = GameDefinition.For(modset.Game);

        if (installation.GameType != modset.Game)
        {
            checks.Add(new LaunchCheckItem(
                "Spielzuordnung",
                "Das ausgewählte Modset gehört nicht zur erkannten Spielinstallation.",
                LaunchCheckSeverity.Error));
        }
        else
        {
            checks.Add(new LaunchCheckItem(
                "Spielzuordnung",
                definition.DisplayName,
                LaunchCheckSeverity.Success));
        }

        if (File.Exists(installation.ExecutablePath))
        {
            checks.Add(new LaunchCheckItem(
                "Spiel-Executable",
                installation.ExecutablePath,
                LaunchCheckSeverity.Success));
        }
        else
        {
            checks.Add(new LaunchCheckItem(
                "Spiel-Executable",
                "Die x64-Spiel-Executable wurde nicht gefunden.",
                LaunchCheckSeverity.Error));
        }

        var homeBasePath = Path.GetFullPath(modset.HomeBasePath);
        if (!Directory.Exists(homeBasePath))
        {
            checks.Add(new LaunchCheckItem(
                "Home-Basis",
                "Das Home-Basisverzeichnis existiert nicht.",
                LaunchCheckSeverity.Error));
        }
        else if (string.Equals(
                     new DirectoryInfo(homeBasePath).Name,
                     definition.HomeDirectoryName,
                     StringComparison.OrdinalIgnoreCase))
        {
            checks.Add(new LaunchCheckItem(
                "Home-Basis",
                $"Der -homedir-Pfad muss auf den Ordner oberhalb von '{definition.HomeDirectoryName}' zeigen.",
                LaunchCheckSeverity.Error));
        }
        else
        {
            checks.Add(CheckWriteAccess(homeBasePath));
        }

        var gameDataDirectory = Path.Combine(homeBasePath, definition.HomeDirectoryName);
        checks.Add(Directory.Exists(gameDataDirectory)
            ? new LaunchCheckItem(
                "SCS-Datenordner",
                gameDataDirectory,
                LaunchCheckSeverity.Success)
            : new LaunchCheckItem(
                "SCS-Datenordner",
                $"'{definition.HomeDirectoryName}' ist noch nicht vorhanden und wird beim ersten Spielstart von SCS angelegt.",
                LaunchCheckSeverity.Warning));

        IReadOnlyList<string> additionalArguments;
        try
        {
            additionalArguments = CommandLineArgumentParser.Parse(modset.AdditionalLaunchArguments);
            var managedArgument = additionalArguments.FirstOrDefault(IsManagedHomeArgument);
            if (managedArgument is not null)
            {
                checks.Add(new LaunchCheckItem(
                    "Startparameter",
                    "-homedir wird vom NMC SCS LAUNCHER verwaltet und darf nicht als zusätzlicher Parameter angegeben werden.",
                    LaunchCheckSeverity.Error));
            }
            else
            {
                checks.Add(new LaunchCheckItem(
                    "Startparameter",
                    additionalArguments.Count == 0 ? "Keine zusätzlichen Startparameter." : $"{additionalArguments.Count} zusätzliche Parameter geprüft.",
                    LaunchCheckSeverity.Success));
            }
        }
        catch (ArgumentException ex)
        {
            additionalArguments = Array.Empty<string>();
            checks.Add(new LaunchCheckItem("Startparameter", ex.Message, LaunchCheckSeverity.Error));
        }

        if (IsGameRunning(definition.ProcessName))
        {
            checks.Add(new LaunchCheckItem(
                "Laufender Prozess",
                $"{definition.DisplayName} scheint bereits zu laufen. Ein zweiter Start kann Probleme verursachen.",
                LaunchCheckSeverity.Warning));
        }
        else
        {
            checks.Add(new LaunchCheckItem(
                "Laufender Prozess",
                "Keine laufende Instanz erkannt.",
                LaunchCheckSeverity.Success));
        }

        return Task.FromResult(new GameLaunchPlan(
            modset,
            installation,
            gameDataDirectory,
            additionalArguments,
            checks));
    }

    public Task<int> LaunchAsync(GameLaunchPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        if (!plan.CanLaunch)
        {
            throw new InvalidOperationException("Der Spielstart ist wegen mindestens eines kritischen Prüfungsfehlers gesperrt.");
        }

        var executablePath = plan.Installation.ExecutablePath;
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Die Spiel-Executable wurde vor dem Start entfernt oder verschoben.", executablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? plan.Installation.InstallPath,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-homedir");
        startInfo.ArgumentList.Add(plan.Modset.HomeBasePath);

        foreach (var argument in plan.AdditionalArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Der Spielprozess konnte nicht gestartet werden.");

        return Task.FromResult(process.Id);
    }

    private static LaunchCheckItem CheckWriteAccess(string directory)
    {
        var probeFile = Path.Combine(directory, $".nmc-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probeFile, 1, FileOptions.DeleteOnClose))
            {
            }

            return new LaunchCheckItem(
                "Schreibrechte",
                "Home-Basis ist beschreibbar.",
                LaunchCheckSeverity.Success);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return new LaunchCheckItem(
                "Schreibrechte",
                "Der Launcher bzw. das Spiel kann nicht in das Home-Basisverzeichnis schreiben.",
                LaunchCheckSeverity.Error);
        }
        finally
        {
            try
            {
                if (File.Exists(probeFile))
                {
                    File.Delete(probeFile);
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }
    }

    private static bool IsManagedHomeArgument(string argument) =>
        string.Equals(argument, "-homedir", StringComparison.OrdinalIgnoreCase) ||
        argument.StartsWith("-homedir=", StringComparison.OrdinalIgnoreCase);

    private static bool IsGameRunning(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
