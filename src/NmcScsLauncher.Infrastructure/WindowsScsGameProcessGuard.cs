using System.Diagnostics;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class WindowsScsGameProcessGuard : IScsGameProcessGuard
{
    public bool IsGameRunning(GameType gameType)
    {
        var processName = GameDefinition.For(gameType).ProcessName;
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(processName);
        }
        catch
        {
            return true;
        }

        try
        {
            return processes.Any(process =>
            {
                try
                {
                    return !process.HasExited;
                }
                catch
                {
                    return true;
                }
            });
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }
}
