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
        services.AddSingleton<IWorkshopContentLocator, SteamWorkshopContentLocator>();
        services.AddSingleton<IWorkshopMetadataProvider, SteamWorkshopMetadataProvider>();
        services.AddSingleton<ScsPlainTextSaveCodec>();
        services.AddSingleton<IScsSaveDecoder, DecryptTruckSaveDecoder>();
        services.AddSingleton<IScsSaveCodec, ScsSaveCodec>();
        services.AddSingleton<IScsGameProcessGuard, ScsGameProcessGuard>();
        services.AddSingleton<IScsSaveEditService, ScsSaveEditService>();
        services.AddSingleton<IScsProfileSaveLocator, ScsProfileSaveLocator>();
        services.AddSingleton<IScsProfileEditService, ScsProfileEditService>();
        services.AddSingleton<IScsVehicleEditService, ScsVehicleEditService>();

        services.AddSingleton(LicenseHubRuntimeConfiguration.FromEnvironment(LicenseEnforcementPolicy.RequiredByBuild));
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IMachineIdentityProvider, WindowsMachineIdentityProvider>();
        services.AddSingleton<ILicenseCredentialStore, WindowsCredentialManagerLicenseStore>();
        services.AddSingleton<IProductApiCredentialStore, WindowsCredentialManagerProductApiKeyStore>();
        services.AddSingleton<ILicenseRuntimeService, LicenseRuntimeService>();
        services.AddSingleton<IApplicationUpdateService, ApplicationUpdateService>();
        services.AddSingleton<ILicenseActivationDialogService, LicenseActivationDialogService>();
        services.AddSingleton<ILicenseDeactivationService, LicenseDeactivationService>();
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
        services.AddSingleton<IExternalUriService, ExternalUriService>();
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
        viewModel.ConfigureWorkshopServices(
            _serviceProvider.GetRequiredService<IWorkshopContentLocator>(),
            _serviceProvider.GetRequiredService<IWorkshopMetadataProvider>(),
            _serviceProvider.GetRequiredService<IExternalUriService>());

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

                if (!licenseState.AllowsUse)
                {
                    await logger.WriteAsync(
                        "WARN",
                        "LicenseHub enforcement is enabled and no valid license was confirmed. Launcher startup was stopped before the main window was shown.");
                    Shutdown();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                await logger.WriteAsync(
                    "ERROR",
                    "LicenseHub startup gate failed. Launcher startup was stopped fail-closed.",
                    ex);
            }
            catch
            {
                // The license gate must remain fail-closed even if logging itself fails.
            }

            MessageBox.Show(
                "Die LicenseHub-Lizenzprüfung konnte nicht sicher abgeschlossen werden.\n\nDer NMC SCS LAUNCHER wird deshalb nicht gestartet.",
                "NMC SCS LAUNCHER – Lizenzprüfung",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        try
        {
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

        await CheckForApplicationUpdateAsync(mainWindow, logger);
    }

    private async Task CheckForApplicationUpdateAsync(Window owner, IAppLogger logger)
    {
        if (_serviceProvider is null) return;

        var updateService = _serviceProvider.GetRequiredService<IApplicationUpdateService>();
        if (!updateService.IsConfigured)
        {
            await logger.WriteAsync("INFO", "LicenseHub update check skipped because update integration is not configured.");
            return;
        }

        try
        {
            var update = await updateService.CheckAsync(AppVersionInfo.Current);
            await logger.WriteAsync(
                "INFO",
                $"LicenseHub update check completed. Current={update.CurrentVersion}; Latest={update.LatestVersion}; Available={update.UpdateAvailable}; Mandatory={update.Mandatory}");

            if (!update.UpdateAvailable) return;

            var window = new ApplicationUpdateWindow(updateService, logger, update)
            {
                Owner = owner
            };
            window.Show();
        }
        catch (Exception ex)
        {
            await logger.WriteAsync("ERROR", "LicenseHub update check failed. Launcher remains available.", ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
