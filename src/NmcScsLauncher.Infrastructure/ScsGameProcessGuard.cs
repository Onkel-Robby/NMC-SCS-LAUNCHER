using System.Diagnostics;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsGameProcessGuard : IScsGameProcessGuard
{
    private static readonly string[] ProcessNames =
    [
        "eurotrucks2",
        "amtrucks"
    ];

    public bool IsAnyScsGameRunning()
    {
        foreach (var processName in ProcessNames)
        {
            try
            {
                using var process = Process.GetProcessesByName(processName).FirstOrDefault();
                if (process is not null && !process.HasExited)
                    return true;
            }
            catch
            {
                // A failed process query must not silently authorize writes.
                return true;
            }
        }

        return false;
    }
}
