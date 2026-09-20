using System.Net.Http;
using System.Windows;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;

namespace NmcScsLauncher.App.Services;

public interface ILicenseDeactivationService
{
    Task<bool> DeactivateCurrentComputerAsync(CancellationToken cancellationToken = default);
}

public sealed class LicenseDeactivationService : ILicenseDeactivationService
{
    private readonly ILicenseRuntimeService _licenseRuntime;
    private readonly IAppLogger _logger;

    public LicenseDeactivationService(
        ILicenseRuntimeService licenseRuntime,
        IAppLogger logger)
    {
        _licenseRuntime = licenseRuntime ?? throw new ArgumentNullException(nameof(licenseRuntime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> DeactivateCurrentComputerAsync(CancellationToken cancellationToken = default)
    {
        var confirmation = MessageBox.Show(
            "Die LicenseHub-Aktivierung dieses PCs wirklich deaktivieren?\n\n" +
            "Dadurch wird die Geräteaktivierung bei LicenseHub freigegeben und der lokal gespeicherte Lizenz-Key erst nach erfolgreicher Server-Deaktivierung entfernt. " +
            "Der NMC SCS LAUNCHER wird anschließend beendet.",
            "LicenseHub-Lizenz deaktivieren",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirmation != MessageBoxResult.Yes)
            return false;

        try
        {
            var state = await _licenseRuntime.DeactivateAsync(cancellationToken);
            if (state.State != LicenseRuntimeState.LicenseMissing)
                throw new InvalidOperationException("LicenseHub deactivation did not produce the expected local license state.");

            await _logger.WriteAsync(
                "INFO",
                "LicenseHub license deactivated for the current computer and the stored user license credential was cleared.");

            MessageBox.Show(
                "Die Lizenz wurde für diesen PC erfolgreich deaktiviert.\n\n" +
                "Beim nächsten Start muss wieder ein gültiger Lizenz-Key eingegeben werden.",
                "LicenseHub-Lizenz deaktiviert",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            Application.Current?.Shutdown();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await _logger.WriteAsync(
                "ERROR",
                "LicenseHub license deactivation failed. The stored license credential was retained unless the server had already confirmed deactivation.",
                ex);

            MessageBox.Show(
                SafeErrorMessage(ex),
                "LicenseHub-Deaktivierung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private static string SafeErrorMessage(Exception exception) => exception switch
    {
        LicenseHubConfigurationException => "LicenseHub ist für diesen Build nicht vollständig konfiguriert. Die lokale Lizenz wurde nicht entfernt.",
        LicenseHubProtocolException => "LicenseHub hat die Deaktivierung nicht bestätigt. Die lokale Lizenz wurde nicht entfernt.",
        HttpRequestException => "LicenseHub ist derzeit nicht erreichbar. Die lokale Lizenz wurde nicht entfernt.",
        IOException => "Die lokale Lizenz konnte nicht sicher aktualisiert werden.",
        _ => "Die Lizenz konnte nicht sicher deaktiviert werden. Details wurden protokolliert."
    };
}
