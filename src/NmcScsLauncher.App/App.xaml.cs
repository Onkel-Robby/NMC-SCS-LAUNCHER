using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NmcScsLauncher.App.Services;
using NmcScsLauncher.App.ViewModels;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;

namespace NmcScsLauncher.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var services = new ServiceCollection();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IAppLogger, FileAppLogger>();
        services.AddSingleton<SteamLibraryLocator>();
        services.AddSingleton<IGameInstallationDetector, SteamGameInstallationDetector>();

        services.AddSingleton(LicenseHubRuntimeConfiguration.FromEnvironment());
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IMachineIdentityProvider, WindowsMachineIdentityProvider>();
        services.AddSingleton<ILicenseCredentialStore, WindowsCredentialManagerLicenseStore>();
        services.AddSingleton<ILicenseRuntimeService, LicenseRuntimeService>();
        services.AddSingleton<ILicenseActivationDialogService, LicenseActivationDialogService>();
        services.AddSingleton<ScsGameLaunchService>();
        services.AddSingleton<LicensedGameLaunchService>();
        services.AddSingleton<IGameLaunchService, InteractiveLicensedGameLaunchService>();

        services.AddSingleton<IModsetInspector, ScsModsetInspector>();
        services.AddSingleton<IModsetStore, JsonModsetStore>();
        services.AddSingleton<IModsetManager, ModsetManager>();
        services.AddSingleton<IModsetDuplicationService, ModsetDuplicationService>();
        services.AddSingleton<IModsetBackupService, ModsetBackupService>();
        services.AddSingleton<IModsetRestoreService, ModsetRestoreService>();
        services.AddSingleton<IFolderPicker, FolderPicker>();
        services.AddSingleton<IModsetEditorService, ModsetEditorService>();
        services.AddSingleton<IModsetDuplicationDialogService, ModsetDuplicationDialogService>();
        services.AddSingleton<IModsetBackupDialogService, ModsetBackupDialogService>();
        services.AddSingleton<IConfirmationService, ConfirmationService>();
        services.AddSingleton<IExplorerService, ExplorerService>();
        services.AddSingleton<IStartCheckDialogService, StartCheckDialogService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        var logger = _serviceProvider.GetRequiredService<IAppLogger>();
        var settingsStore = _serviceProvider.GetRequiredService<ISettingsStore>();
        var licenseRuntime = _serviceProvider.GetRequiredService<ILicenseRuntimeService>();
        var activationDialog = _serviceProvider.GetRequiredService<ILicenseActivationDialogService>();
        var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
        viewModel.ConfigureDuplicationServices(
            _serviceProvider.GetRequiredService<IModsetDuplicationService>(),
            _serviceProvider.GetRequiredService<IModsetDuplicationDialogService>());
        viewModel.ConfigureBackupServices(
            _serviceProvider.GetRequiredService<IModsetBackupService>(),
            _serviceProvider.GetRequiredService<IModsetRestoreService>(),
            _serviceProvider.GetRequiredService<IModsetBackupDialogService>());

        try
        {
            await logger.WriteAsync("INFO", "NMC SCS LAUNCHER started.");
            var licenseState = await licenseRuntime.InitializeAsync(AppVersionInfo.Current);
            await logger.WriteAsync(
                "INFO",
                $"LicenseHub runtime initialized. State={licenseState.State}; Enforcement={licenseState.EnforcementEnabled}; AllowsUse={licenseState.AllowsUse}");

            if (licenseState.EnforcementEnabled && !licenseState.AllowsUse)
            {
                _ = activationDialog.Show(licenseRuntime, licenseState, AppVersionInfo.Current);
                licenseState = licenseRuntime.Current;
                await logger.WriteAsync(
                    "INFO",
                    $"LicenseHub activation dialog closed. State={licenseState.State}; AllowsUse={licenseState.AllowsUse}");
            }

            var settings = await settingsStore.LoadAsync();
            await viewModel.InitializeAsync(settings);
        }
        catch (Exception ex)
        {
            viewModel.SetStartupError("Die Initialisierung konnte nicht vollständig abgeschlossen werden.");
            await logger.WriteAsync("ERROR", "Startup initialization failed.", ex);
        }

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
