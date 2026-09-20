using System.IO;
using WixToolset.BootstrapperApplicationApi;

namespace NmcScsLauncher.Installer;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ManagedBootstrapperApplication.Run(new NmcInstallerBootstrapperApplication());
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "NMC-SCS-LAUNCHER-Installer.failure.log"),
                    ex.ToString());
            }
            catch
            {
                // Diagnostic logging must never hide the original failure.
            }

            return ex.HResult;
        }
    }
}
