using System.Windows;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.Services;

public interface ILicenseActivationDialogService
{
    bool Show(ILicenseRuntimeService runtime, LicenseRuntimeSnapshot initialState, string appVersion);
}

public sealed class LicenseActivationDialogService : ILicenseActivationDialogService
{
    private readonly IProductApiCredentialStore _productApiCredentialStore;

    public LicenseActivationDialogService(IProductApiCredentialStore productApiCredentialStore)
    {
        _productApiCredentialStore = productApiCredentialStore ?? throw new ArgumentNullException(nameof(productApiCredentialStore));
    }

    public bool Show(ILicenseRuntimeService runtime, LicenseRuntimeSnapshot initialState, string appVersion)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(initialState);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);

        var window = new LicenseActivationWindow(runtime, _productApiCredentialStore, initialState, appVersion);
        if (Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true;
    }
}
