using System.Windows;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;

namespace NmcScsLauncher.App;

public partial class ApplicationUpdateWindow : Window
{
    private readonly IApplicationUpdateService _updateService;
    private readonly IAppLogger _logger;
    private readonly LicenseHubUpdateInfo _update;
    private PreparedApplicationUpdate? _preparedUpdate;
    private bool _busy;

    public ApplicationUpdateWindow(
        IApplicationUpdateService updateService,
        IAppLogger logger,
        LicenseHubUpdateInfo update)
    {
        InitializeComponent();
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _update = update ?? throw new ArgumentNullException(nameof(update));

        CurrentVersionTextBlock.Text = update.CurrentVersion;
        LatestVersionTextBlock.Text = update.LatestVersion;
        ChangelogTextBlock.Text = string.IsNullOrWhiteSpace(update.Changelog)
            ? "Für diese Version wurde kein Changelog hinterlegt."
            : update.Changelog.Trim();
        ReleaseDateTextBlock.Text = update.ReleasedAt is { } releasedAt
            ? $"Veröffentlicht: {releasedAt.ToLocalTime():dd.MM.yyyy HH:mm}"
            : string.Empty;
        MandatoryTextBlock.Visibility = update.Mandatory ? Visibility.Visible : Visibility.Collapsed;
        StatusTextBlock.Text = "Das Update wurde von LicenseHub angekündigt. Der Download wird vor der Installation per SHA-256 geprüft.";
    }

    private async void Download_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _preparedUpdate is not null) return;

        SetBusy(true);
        DownloadProgressBar.Value = 0;
        DownloadProgressBar.Visibility = Visibility.Visible;
        StatusTextBlock.Text = "Update wird über den signierten LicenseHub-Download geladen …";

        try
        {
            var progress = new Progress<double>(value =>
            {
                DownloadProgressBar.Value = value;
                StatusTextBlock.Text = $"Update wird heruntergeladen und geprüft … {value:0}%";
            });

            _preparedUpdate = await _updateService.DownloadAsync(_update, progress);
            DownloadProgressBar.Value = 100;
            DownloadButton.IsEnabled = false;
            InstallButton.IsEnabled = true;
            StatusTextBlock.Text = "Download abgeschlossen. SHA-256 wurde erfolgreich verifiziert. Das Update ist installationsbereit.";
            await _logger.WriteAsync(
                "INFO",
                $"LicenseHub update downloaded and verified. Version={_update.LatestVersion}; Bytes={_preparedUpdate.Download.Length}");
        }
        catch (Exception ex)
        {
            _preparedUpdate = null;
            InstallButton.IsEnabled = false;
            DownloadProgressBar.Visibility = Visibility.Collapsed;
            StatusTextBlock.Text = SafeErrorMessage(ex);
            await _logger.WriteAsync("ERROR", "LicenseHub update download or verification failed.", ex);
        }
        finally
        {
            SetBusy(false);
            if (_preparedUpdate is null) DownloadButton.IsEnabled = true;
        }
    }

    private async void Install_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _preparedUpdate is null) return;

        SetBusy(true);
        LaterButton.IsEnabled = false;
        StatusTextBlock.Text = "Externer Updater wird gestartet. Der Launcher wird anschließend beendet …";

        try
        {
            var updaterPid = await _updateService.StartApplyAsync(_preparedUpdate);
            await _logger.WriteAsync(
                "INFO",
                $"External updater started. Version={_update.LatestVersion}; ProcessId={updaterPid}");

            StatusTextBlock.Text = "Updater wurde gestartet. NMC SCS LAUNCHER wird jetzt beendet und nach erfolgreichem Austausch automatisch neu gestartet.";
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = SafeErrorMessage(ex);
            LaterButton.IsEnabled = true;
            InstallButton.IsEnabled = true;
            await _logger.WriteAsync("ERROR", "Starting external application updater failed.", ex);
            SetBusy(false);
        }
    }

    private void Later_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_busy) Close();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        DownloadButton.IsEnabled = !busy && _preparedUpdate is null;
        InstallButton.IsEnabled = !busy && _preparedUpdate is not null;
        LaterButton.IsEnabled = !busy;
    }

    private static string SafeErrorMessage(Exception exception) => exception switch
    {
        LicenseHubConfigurationException => "LicenseHub-Updates sind für diesen Build nicht vollständig konfiguriert.",
        LicenseHubProtocolException => "Das Update konnte nicht sicher verifiziert werden. Es wurde nicht installiert.",
        HttpRequestException => "LicenseHub oder der signierte Download ist derzeit nicht erreichbar.",
        OperationCanceledException => "Der Updatevorgang wurde abgebrochen oder hat das Zeitlimit überschritten.",
        FileNotFoundException => "Eine für den Updatevorgang benötigte Datei fehlt.",
        InvalidDataException => "Das Updatepaket ist ungültig oder entspricht nicht dem erwarteten Format.",
        IOException => "Das Update konnte nicht sicher auf dem Datenträger vorbereitet werden.",
        _ => "Das Update konnte nicht abgeschlossen werden. Details wurden protokolliert."
    };
}
