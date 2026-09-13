using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class WindowsMachineIdentityProvider : IMachineIdentityProvider
{
    private const string RegistryPath = @"SOFTWARE\Microsoft\Cryptography";
    private const string RegistryValue = "MachineGuid";
    private const string ProductNamespace = "NMC-SCS-LAUNCHER";

    public Task<string> GetMachineIdAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("NMC SCS LAUNCHER machine identity is only supported on Windows.");

        using var key = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false);
        var machineGuid = key?.GetValue(RegistryValue) as string;
        if (string.IsNullOrWhiteSpace(machineGuid))
            throw new InvalidOperationException("Windows MachineGuid could not be read.");

        var normalized = machineGuid.Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ProductNamespace + "|" + normalized));
        return Task.FromResult(Convert.ToHexString(bytes).ToLowerInvariant());
    }
}
