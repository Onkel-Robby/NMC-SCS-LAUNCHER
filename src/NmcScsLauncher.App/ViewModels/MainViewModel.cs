using CommunityToolkit.Mvvm.ComponentModel;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _statusText = "Lokale Konfiguration wird initialisiert …";

    public string VersionText => "Version 0.1.0-dev";

    public void SetSettingsLoaded(LauncherSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        StatusText = "Projektbasis bereit – Spielinstallationserkennung folgt in Phase 1.";
    }

    public void SetStartupError(string message)
    {
        StatusText = message;
    }
}
