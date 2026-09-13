using System.Windows;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;

namespace NmcScsLauncher.App;

public partial class LicenseActivationWindow : Window
{
    private readonly ILicenseRuntimeService _runtime;
    private readonly string _appVersion;

    public LicenseActivationWindow(
        ILicenseRuntimeService runtime,
        LicenseRuntimeSnapshot initialState,
        string appVersion)
    {
        InitializeComponent();
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _appVersion = string.IsNullOrWhiteSpace(appVersion)
            ? throw new ArgumentException("App version is required.", nameof(appVersion))
            : appVersion;

        ApplyState(initialState ?? throw new ArgumentNullException(nameof(initialState)));
    }

    private async void Activate_OnClick(object sender, RoutedEventArgs e)
    {
        var licenseKey = LicenseKeyPasswordBox.Password.Trim();
        if (licenseKey.Length == 0)
        {
            ResultTextBlock.Text = "Bitte einen Lizenzschlüssel eingeben.";
            return;
        }

        SetBusy(true, "Lizenz wird bei LicenseHub aktiviert …");
        try
        {
            var state = await _runtime.ActivateAsync(
                licenseKey,
                Environment.MachineName,
                _appVersion);
            ApplyState(state);

            if (state.AllowsUse && state.EnforcementEnabled)
            {
                LicenseKeyPasswordBox.Clear();
                ResultTextBlock.Text = "Lizenz wurde erfolgreich aktiviert und bestätigt.";
                DialogResult = true;
            }
        }
        catch (Exception ex)
        {
            ResultTextBlock.Text = $"Aktivierung konnte nicht abgeschlossen werden: {SafeMessage(ex)}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Retry_OnClick(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Lizenzstatus wird erneut geprüft …");
        try
        {
            var state = await _runtime.RefreshAsync(_appVersion);
            ApplyState(state);
            if (state.AllowsUse && state.EnforcementEnabled)
            {
                ResultTextBlock.Text = "Gespeicherte Lizenz wurde von LicenseHub bestätigt.";
                DialogResult = true;
            }
        }
        catch (Exception ex)
        {
            ResultTextBlock.Text = $"Statusprüfung konnte nicht abgeschlossen werden: {SafeMessage(ex)}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyState(LicenseRuntimeSnapshot state)
    {
        StatusTextBlock.Text = state.Message;
        ResultTextBlock.Text = BuildDetails(state);
    }

    private void SetBusy(bool busy, string? message = null)
    {
        ProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ActivateButton.IsEnabled = !busy;
        RetryButton.IsEnabled = !busy;
        LicenseKeyPasswordBox.IsEnabled = !busy;
        if (!string.IsNullOrWhiteSpace(message)) ResultTextBlock.Text = message;
    }

    private static string BuildDetails(LicenseRuntimeSnapshot state)
    {
        var parts = new List<string>();
        if (state.ExpiresAt is { } expiresAt)
            parts.Add($"Gültig bis: {expiresAt.ToLocalTime():dd.MM.yyyy HH:mm}");
        if (state.CurrentActivations is { } current && state.MaxActivations is { } maximum)
            parts.Add($"Aktivierungen: {current}/{maximum}");
        if (!string.IsNullOrWhiteSpace(state.CustomerName))
            parts.Add($"Kunde: {state.CustomerName}");
        return parts.Count == 0 ? string.Empty : string.Join(" · ", parts);
    }

    private static string SafeMessage(Exception exception) => exception switch
    {
        LicenseHubConfigurationException => "LicenseHub ist für diesen Build nicht vollständig konfiguriert.",
        HttpRequestException => "LicenseHub ist derzeit nicht erreichbar.",
        OperationCanceledException => "Die Anfrage wurde abgebrochen oder hat das Zeitlimit überschritten.",
        _ => "Details wurden nicht in der Oberfläche offengelegt."
    };
}
