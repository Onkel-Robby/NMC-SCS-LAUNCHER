using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using WixToolset.BootstrapperApplicationApi;

namespace NmcScsLauncher.Installer;

internal sealed class NmcInstallerBootstrapperApplication : BootstrapperApplication
{
    private const string MsiPackageId = "NmcScsLauncherMsi";
    private const string LegacyUninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{7F42D224-0D68-4E97-B034-65E84D9AD45B}_is1";
    private const string ManagedInstallKey =
        @"Software\NMC IT-SERVICE\NMC SCS LAUNCHER";

    private IBootstrapperCommand? _command;
    private Dispatcher? _dispatcher;
    private InstallerWindow? _window;
    private bool _packageInstalled;
    private bool _actionInProgress;
    private bool _silent;
    private LaunchAction _currentAction = LaunchAction.Install;
    private IntPtr _applyOwner;
    private int _exitCode;
    private string _lastError = string.Empty;
    private ManualResetEventSlim? _silentDone;
    private Thread? _silentOwnerThread;
    private Dispatcher? _silentOwnerDispatcher;
    private LegacyInstallInfo? _legacyInstall;

    protected override void OnCreate(CreateEventArgs args)
    {
        _command = args.Command;
        base.OnCreate(args);
    }

    protected override void Run()
    {
        WireEvents();

        if (_command is not null && _command.Display != Display.Full)
            RunSilent();
        else
            RunInteractive();
    }

    private void WireEvents()
    {
        DetectPackageComplete += (_, e) =>
        {
            if (string.Equals(e.PackageId, MsiPackageId, StringComparison.OrdinalIgnoreCase))
                _packageInstalled = e.State is PackageState.Present or PackageState.Superseded or PackageState.Obsolete;
        };

        DetectComplete += OnDetectComplete;
        PlanComplete += OnPlanComplete;

        ApplyBegin += (_, _) => Ui(() =>
        {
            _window?.SetBusy(true, ActionStatusText(_currentAction));
            _window?.SetProgress(0, "Vorgang gestartet");
        });

        ExecutePackageBegin += (_, e) =>
            Ui(() => _window?.SetStatus($"Paket wird verarbeitet: {e.PackageId}"));

        ExecuteProgress += (_, e) =>
            Ui(() => _window?.SetProgress(e.OverallPercentage, ActionStatusText(_currentAction)));

        Error += (_, e) =>
        {
            _lastError = $"Fehler {e.ErrorCode}: {e.ErrorMessage}";
            Ui(() => _window?.SetStatus(_lastError));
        };

        ApplyComplete += OnApplyComplete;
    }

    private void RunInteractive()
    {
        _legacyInstall = DetectLegacyInnoInstall();

        var managedPath = DetectManagedInstallPath();
        var initialPath = !string.IsNullOrWhiteSpace(managedPath)
            ? managedPath
            : !string.IsNullOrWhiteSpace(_legacyInstall?.InstallLocation)
                ? _legacyInstall!.InstallLocation
                : GetDefaultInstallPath();

        ConfigureVariables(initialPath, createDesktopShortcut: false);

        var uiThread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;

            var app = new System.Windows.Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };

            _window = new InstallerWindow(GetDisplayVersion(), initialPath);
            app.MainWindow = _window;

            _window.PrimaryRequested += (_, _) =>
            {
                AppendDiagnostic($"Primary action requested. Installed={_packageInstalled}, Legacy={_legacyInstall is not null}.");
                BeginInteractiveAction(_packageInstalled ? LaunchAction.Repair : LaunchAction.Install);
            };
            _window.UninstallRequested += (_, _) => BeginInteractiveAction(LaunchAction.Uninstall);
            _window.FinishRequested += (_, _) =>
            {
                if (_window.LaunchAfterFinish && _currentAction != LaunchAction.Uninstall)
                    TryLaunchInstalledApplication();

                _window.Close();
            };
            _window.CancelRequested += (_, _) => _window.Close();

            _window.SetBusy(true, "Vorhandene Installation wird geprüft …");
            _window.Show();
            _applyOwner = new WindowInteropHelper(_window).Handle;
            AppendDiagnostic($"Interactive window shown. ApplyOwner=0x{_applyOwner.ToInt64():X}.");

            engine.Detect();
            app.Run();
        })
        {
            Name = "NmcScsLauncherInstallerUI",
            IsBackground = false
        };

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();
        uiThread.Join();

        engine.Quit(_exitCode);
    }

    private void RunSilent()
    {
        _silent = true;
        _silentDone = new ManualResetEventSlim(false);
        _legacyInstall = DetectLegacyInnoInstall();

        var requested = _command?.Action ?? LaunchAction.Install;
        _currentAction = requested is LaunchAction.Unknown or LaunchAction.Help or LaunchAction.UpdateReplace
            ? LaunchAction.Install
            : requested;

        var managedPath = DetectManagedInstallPath();
        var installPath = !string.IsNullOrWhiteSpace(managedPath)
            ? managedPath
            : !string.IsNullOrWhiteSpace(_legacyInstall?.InstallLocation)
                ? _legacyInstall!.InstallLocation
                : GetDefaultInstallPath();

        ConfigureVariables(installPath, createDesktopShortcut: false);
        CreateSilentOwnerWindow();

        try
        {
            if (_currentAction == LaunchAction.Install && _legacyInstall?.UninstallerPath is not null)
                MigrateLegacyInnoInstallation(_legacyInstall);

            engine.Detect();
            _silentDone.Wait();
        }
        catch (Exception ex)
        {
            _exitCode = ex.HResult != 0 ? ex.HResult : 1;
        }
        finally
        {
            _silentOwnerDispatcher?.BeginInvokeShutdown(DispatcherPriority.Normal);
            _silentOwnerThread?.Join(TimeSpan.FromSeconds(5));
            engine.Quit(_exitCode);
        }
    }

    private void OnDetectComplete(object? sender, DetectCompleteEventArgs e)
    {
        if (e.Status < 0)
        {
            Fail($"Installationserkennung fehlgeschlagen (0x{e.Status:X8}).");
            if (_silent)
                _silentDone?.Set();
            return;
        }

        if (_silent)
        {
            try
            {
                engine.Plan(_currentAction);
            }
            catch (Exception ex)
            {
                _exitCode = ex.HResult != 0 ? ex.HResult : 1;
                _silentDone?.Set();
            }

            return;
        }

        Ui(() =>
        {
            _window?.SetInstalled(_packageInstalled);
            _window?.SetBusy(false,
                _packageInstalled
                    ? "NMC SCS LAUNCHER ist installiert. Reparatur oder Deinstallation ist möglich."
                    : _legacyInstall is not null
                        ? "Bestehende Inno-Installation erkannt. Der Installationspfad wird übernommen."
                        : "Bereit zur Installation.");
            _window?.SetProgress(0, "Bereit");
        });
    }

    private async void BeginInteractiveAction(LaunchAction action)
    {
        if (_actionInProgress)
        {
            AppendDiagnostic("Primary action ignored because another action is already in progress.");
            return;
        }

        AppendDiagnostic($"BeginInteractiveAction: {action}.");
        _actionInProgress = true;

        if (IsLauncherRunning())
        {
            AppendDiagnostic("NmcScsLauncher.App is running; prompting user to close it.");

            var answer = System.Windows.MessageBox.Show(
                _window,
                "NMC SCS LAUNCHER läuft noch.\n\nSoll der Installer den Launcher jetzt automatisch schließen und anschließend fortfahren?\n\nNicht gespeicherte Änderungen im Launcher können dabei verloren gehen.",
                "NMC SCS LAUNCHER",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
            {
                AppendDiagnostic("User declined automatic launcher shutdown.");
                _actionInProgress = false;
                _window?.SetStatus("Installation wartet darauf, dass der Launcher geschlossen wird.");
                return;
            }

            _window?.SetBusy(true, "NMC SCS LAUNCHER wird geschlossen …");
            _window?.SetProgress(1, "Laufender Launcher wird beendet");

            var closed = await Task.Run(TryCloseLauncherProcesses);
            if (!closed)
            {
                AppendDiagnostic("Automatic launcher shutdown failed.");
                Fail(
                    "NMC SCS LAUNCHER konnte nicht automatisch beendet werden. Bitte den Launcher im Task-Manager schließen und danach erneut auf „Installieren“ klicken.");
                return;
            }

            AppendDiagnostic("NmcScsLauncher.App was closed successfully.");
        }

        _currentAction = action;
        _lastError = string.Empty;

        var installPath = _window?.InstallPath ?? GetDefaultInstallPath();
        var desktop = _window?.CreateDesktopShortcut == true;
        ConfigureVariables(installPath, desktop);
        AppendDiagnostic($"Variables configured. InstallFolder='{installPath}', DesktopShortcut={desktop}.");

        _window?.SetBusy(true,
            action == LaunchAction.Uninstall
                ? "Deinstallation wird vorbereitet …"
                : action == LaunchAction.Repair
                    ? "Reparatur wird vorbereitet …"
                    : "Installation wird vorbereitet …");
        _window?.SetProgress(1, "Vorgang wird vorbereitet");

        if (action == LaunchAction.Install && !_packageInstalled && _legacyInstall?.UninstallerPath is not null)
        {
            var legacy = _legacyInstall;
            try
            {
                _window?.SetStatus("Bestehende Inno-Installation wird entfernt …");
                _window?.SetProgress(2, "Vorherige Installation wird entfernt");
                AppendDiagnostic($"Starting legacy Inno migration from '{legacy.InstallLocation}'.");

                await Task.Run(() => MigrateLegacyInnoInstallation(legacy));

                _legacyInstall = null;
                AppendDiagnostic("Legacy Inno migration completed.");
                _window?.SetStatus("Installation wird geplant …");
                _window?.SetProgress(5, "Installation wird geplant");
            }
            catch (Exception ex)
            {
                AppendDiagnostic($"Legacy Inno migration failed: {ex}");
                Fail($"Die bisherige Installation konnte nicht entfernt werden: {ex.Message}");
                return;
            }
        }

        PlanInteractiveAction();
    }

    private void PlanInteractiveAction()
    {
        try
        {
            AppendDiagnostic($"Calling Burn Plan({_currentAction}).");
            engine.Plan(_currentAction);
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"Burn Plan failed: {ex}");
            Fail($"Planung des Installationsvorgangs fehlgeschlagen: {ex.Message}");
        }
    }

    private void OnPlanComplete(object? sender, PlanCompleteEventArgs e)
    {
        if (e.Status < 0)
        {
            Fail($"Installationsplanung fehlgeschlagen (0x{e.Status:X8}).");
            if (_silent)
                _silentDone?.Set();
            return;
        }

        try
        {
            AppendDiagnostic($"Plan completed successfully. Calling Burn Apply with owner=0x{_applyOwner.ToInt64():X}.");
            engine.Apply(_applyOwner);
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"Burn Apply failed: {ex}");
            Fail($"Installation konnte nicht gestartet werden: {ex.Message}");
            if (_silent)
                _silentDone?.Set();
        }
    }

    private void OnApplyComplete(object? sender, ApplyCompleteEventArgs e)
    {
        AppendDiagnostic($"Apply complete. Status=0x{e.Status:X8}, Action={_currentAction}.");
        _actionInProgress = false;
        _exitCode = e.Status;

        if (_silent)
        {
            _silentDone?.Set();
            return;
        }

        if (e.Status >= 0)
        {
            _packageInstalled = _currentAction != LaunchAction.Uninstall;
            Ui(() =>
            {
                _window?.SetInstalled(_packageInstalled);
                _window?.ShowSuccess(
                    _packageInstalled,
                    _currentAction == LaunchAction.Uninstall
                        ? "NMC SCS LAUNCHER wurde deinstalliert. Lokale Launcher-Daten und LicenseHub-Credentials wurden nicht gelöscht."
                        : "NMC SCS LAUNCHER wurde erfolgreich installiert bzw. repariert.");
            });
            return;
        }

        var detail = string.IsNullOrWhiteSpace(_lastError)
            ? $"WiX/Burn meldete Status 0x{e.Status:X8}."
            : _lastError;
        Fail(detail);
    }

    private void ConfigureVariables(string installPath, bool createDesktopShortcut)
    {
        engine.SetVariableString("InstallFolder", installPath, false);
        engine.SetVariableNumeric("CreateDesktopShortcut", createDesktopShortcut ? 1 : 0);
    }

    private void Fail(string message)
    {
        AppendDiagnostic($"FAIL: {message}");
        _actionInProgress = false;
        _exitCode = _exitCode == 0 ? 1 : _exitCode;
        Ui(() => _window?.ShowFailure(message));
    }

    private static void AppendDiagnostic(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "NMC-SCS-LAUNCHER-Installer.log");
            File.AppendAllText(
                path,
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never affect installer execution.
        }
    }

    private void Ui(Action action)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null)
            return;

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }

    private static string GetDefaultInstallPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "NMC SCS LAUNCHER");

    private static string GetDisplayVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static bool IsLauncherRunning()
    {
        try
        {
            return Process.GetProcessesByName("NmcScsLauncher.App").Any(p => !p.HasExited);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCloseLauncherProcesses()
    {
        try
        {
            var processes = Process.GetProcessesByName("NmcScsLauncher.App");
            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited)
                        continue;

                    AppendDiagnostic($"Attempting graceful launcher shutdown. PID={process.Id}.");
                    if (process.CloseMainWindow() && process.WaitForExit((int)TimeSpan.FromSeconds(5).TotalMilliseconds))
                        continue;

                    if (!process.HasExited)
                    {
                        AppendDiagnostic($"Graceful shutdown timed out; terminating launcher. PID={process.Id}.");
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit((int)TimeSpan.FromSeconds(5).TotalMilliseconds);
                    }
                }
                catch (Exception ex)
                {
                    AppendDiagnostic($"Failed to close launcher process PID={process.Id}: {ex.Message}");
                }
                finally
                {
                    process.Dispose();
                }
            }

            var remaining = Process.GetProcessesByName("NmcScsLauncher.App");
            try
            {
                return remaining.All(p => p.HasExited);
            }
            finally
            {
                foreach (var process in remaining)
                    process.Dispose();
            }
        }
        catch (Exception ex)
        {
            AppendDiagnostic($"Launcher shutdown check failed: {ex}");
            return false;
        }
    }

    private void TryLaunchInstalledApplication()
    {
        try
        {
            var installPath = engine.GetVariableString("InstallFolder");
            var exe = Path.Combine(installPath, "NmcScsLauncher.App.exe");
            if (!File.Exists(exe))
                return;

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = installPath,
                UseShellExecute = true
            });
        }
        catch
        {
            // Installation is already complete; launching is best-effort only.
        }
    }

    private static string? DetectManagedInstallPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ManagedInstallKey);
            return key?.GetValue("InstallPath") as string;
        }
        catch
        {
            return null;
        }
    }

    private static LegacyInstallInfo? DetectLegacyInnoInstall()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(LegacyUninstallKey);
                if (key is null)
                    continue;

                var location = (key.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                if (string.IsNullOrWhiteSpace(location))
                    continue;

                var uninstaller = Path.Combine(location, "unins000.exe");
                return new LegacyInstallInfo(
                    location.TrimEnd(Path.DirectorySeparatorChar),
                    File.Exists(uninstaller) ? uninstaller : null);
            }
            catch
            {
                // Try the next hive.
            }
        }

        return null;
    }

    private static void MigrateLegacyInnoInstallation(LegacyInstallInfo legacy)
    {
        if (legacy.UninstallerPath is null || !File.Exists(legacy.UninstallerPath))
        {
            AppendDiagnostic("Legacy migration skipped because no Inno uninstaller exists.");
            return;
        }

        AppendDiagnostic($"Launching Inno uninstaller '{legacy.UninstallerPath}'.");
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = legacy.UninstallerPath,
            Arguments = "/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
            UseShellExecute = true,
            WorkingDirectory = legacy.InstallLocation
        }) ?? throw new InvalidOperationException("Der bisherige Inno-Uninstaller konnte nicht gestartet werden.");

        if (!process.WaitForExit((int)TimeSpan.FromMinutes(3).TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("Die Deinstallation der bisherigen Inno-Version hat das Zeitlimit überschritten.");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"Die bisherige Inno-Installation konnte nicht entfernt werden (Exit-Code {process.ExitCode}).");

        AppendDiagnostic("Inno uninstaller exited successfully.");
    }

    private void CreateSilentOwnerWindow()
    {
        using var ready = new ManualResetEventSlim(false);

        _silentOwnerThread = new Thread(() =>
        {
            _silentOwnerDispatcher = Dispatcher.CurrentDispatcher;
            var owner = new Window
            {
                Width = 1,
                Height = 1,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Opacity = 0
            };

            owner.Show();
            _applyOwner = new WindowInteropHelper(owner).Handle;
            owner.Hide();
            ready.Set();

            Dispatcher.Run();
            owner.Close();
        })
        {
            Name = "NmcInstallerSilentOwner",
            IsBackground = false
        };

        _silentOwnerThread.SetApartmentState(ApartmentState.STA);
        _silentOwnerThread.Start();
        ready.Wait();
    }

    private static string ActionStatusText(LaunchAction action) =>
        action switch
        {
            LaunchAction.Uninstall => "NMC SCS LAUNCHER wird deinstalliert …",
            LaunchAction.Repair => "NMC SCS LAUNCHER wird repariert …",
            _ => "NMC SCS LAUNCHER wird installiert …"
        };

    private sealed record LegacyInstallInfo(string InstallLocation, string? UninstallerPath);
}
