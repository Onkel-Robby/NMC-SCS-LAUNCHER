using System.ComponentModel;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;

namespace NmcScsLauncher.Installer;

public partial class InstallerWindow : Window
{
    private bool _installed;
    private bool _busy;

    public InstallerWindow(string version, string initialInstallPath)
    {
        InitializeComponent();
        VersionText.Text = $"Version {version}";
        InstallPathTextBox.Text = initialInstallPath;
    }

    public event EventHandler? PrimaryRequested;
    public event EventHandler? UninstallRequested;
    public event EventHandler? FinishRequested;
    public event EventHandler? CancelRequested;

    public string InstallPath => InstallPathTextBox.Text.Trim();
    public bool CreateDesktopShortcut => DesktopShortcutCheckBox.IsChecked == true;
    public bool LaunchAfterFinish => LaunchAfterFinishCheckBox.IsChecked == true;

    public void SetInstalled(bool installed)
    {
        _installed = installed;
        InstalledStateText.Text = installed ? "Installiert" : "Nicht installiert";
        TitleText.Text = installed ? "NMC SCS LAUNCHER verwalten" : "NMC SCS LAUNCHER installieren";
        PrimaryButton.Content = installed ? "Reparieren" : "Installieren";
        UninstallButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        InstallPathTextBox.IsEnabled = !installed && !_busy;
        BrowseButton.IsEnabled = !installed && !_busy;
        DesktopShortcutCheckBox.IsEnabled = !_busy;
    }

    public void SetBusy(bool busy, string status)
    {
        _busy = busy;
        StatusText.Text = status;
        PrimaryButton.IsEnabled = !busy;
        UninstallButton.IsEnabled = !busy;
        CancelButton.IsEnabled = !busy;
        InstallPathTextBox.IsEnabled = !busy && !_installed;
        BrowseButton.IsEnabled = !busy && !_installed;
        DesktopShortcutCheckBox.IsEnabled = !busy;
        ProgressBar.IsIndeterminate = busy && ProgressBar.Value <= 0;
        Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
    }

    public void SetProgress(int percent, string detail)
    {
        var safe = Math.Clamp(percent, 0, 100);
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = safe;
        ProgressText.Text = $"{safe}% – {detail}";
    }

    public void SetStatus(string status)
    {
        StatusText.Text = status;
    }

    public void ShowSuccess(bool installed, string text)
    {
        _busy = false;
        ProgressBar.IsIndeterminate = false;
        ProgressBar.Value = 100;
        ProgressText.Text = "100% – abgeschlossen";
        ResultPanel.Visibility = Visibility.Visible;
        ResultTitleText.Text = "Vorgang erfolgreich abgeschlossen";
        ResultBodyText.Text = text;
        LaunchAfterFinishCheckBox.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Visibility = Visibility.Collapsed;
        UninstallButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        FinishButton.Visibility = Visibility.Visible;
        Cursor = System.Windows.Input.Cursors.Arrow;
    }

    public void ShowFailure(string text)
    {
        _busy = false;
        ProgressBar.IsIndeterminate = false;
        ResultPanel.Visibility = Visibility.Visible;
        ResultTitleText.Text = "Vorgang konnte nicht abgeschlossen werden";
        ResultBodyText.Text = text;
        LaunchAfterFinishCheckBox.Visibility = Visibility.Collapsed;
        PrimaryButton.Visibility = Visibility.Visible;
        CancelButton.Visibility = Visibility.Visible;
        FinishButton.Visibility = Visibility.Collapsed;
        SetInstalled(_installed);
        Cursor = System.Windows.Input.Cursors.Arrow;
    }

    private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Installationsordner für NMC SCS LAUNCHER auswählen",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(InstallPath) ? InstallPath : string.Empty,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            InstallPathTextBox.Text = dialog.SelectedPath;
    }

    private void PrimaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_installed)
        {
            try
            {
                var full = Path.GetFullPath(InstallPath);
                if (!Path.IsPathFullyQualified(full))
                    throw new InvalidOperationException();

                InstallPathTextBox.Text = full.TrimEnd(Path.DirectorySeparatorChar);
            }
            catch
            {
                ShowFailure("Bitte einen gültigen absoluten Installationspfad auswählen.");
                return;
            }
        }

        PrimaryRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UninstallButton_OnClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "NMC SCS LAUNCHER wirklich deinstallieren?\n\nLauncher-Daten und LicenseHub-Credentials bleiben erhalten.",
            "NMC SCS LAUNCHER",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
            UninstallRequested?.Invoke(this, EventArgs.Empty);
    }

    private void FinishButton_OnClick(object sender, RoutedEventArgs e) =>
        FinishRequested?.Invoke(this, EventArgs.Empty);

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) =>
        CancelRequested?.Invoke(this, EventArgs.Empty);

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            MessageBox.Show(
                this,
                "Der Installationsvorgang läuft noch. Bitte warten, bis er abgeschlossen ist.",
                "NMC SCS LAUNCHER",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
