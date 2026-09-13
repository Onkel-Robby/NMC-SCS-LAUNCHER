using System.Windows;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.Services;

public interface ILicenseActivationDialogService
{
    bool Show(ILicenseRuntimeService runtime, LicenseRuntimeSnapshot initialState, string appVersion);
}

public sealed class LicenseActivationDialogService : ILicenseActivationDialogService
{
    public bool Show(ILicenseRuntimeService runtime, LicenseRuntimeSnapshot initialState, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);

        var window = new LicenseActivationWindow(runtime, initialState, appVersion);
        if (Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true;
    }
}
