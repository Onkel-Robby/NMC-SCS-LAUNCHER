using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.App.ViewModels;
using NmcScsLauncher.App.Views;
using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.App.Tests;

public sealed partial class TruckPowertrainSelectionTests
{
    [Fact]
    public async Task InstallDisablesNoOpAndSubmitsBoundPairThenRefreshesBackupAndPreview()
    {
        var (harness, service) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        var installer = new Installer();
        ConfigureInstaller(vm, installer, new ProcessGuard());
        var command = InstallCommand(vm);
        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync(null);
        Assert.Empty(installer.Requests);
        SelectChangedPair(vm);
        Assert.True(command.CanExecute(null));
        installer.Handler = request =>
        {
            Assert.True(vm.IsBusy);
            Assert.Empty(vm.TruckPowertrainEngines);
            return Task.FromResult(Installed(request));
        };
        await command.ExecuteAsync(null);
        await Loaded(vm);
        var request = Assert.Single(installer.Requests);
        Assert.Equal(new string('A', 64), request.ExpectedSaveSha256);
        Assert.Equal("truck", request.ExpectedTruckId);
        Assert.Equal(Model + "engine/boost.sii", request.EngineDataPath);
        Assert.Equal(Model + "transmission/sport.sii", request.TransmissionDataPath);
        Assert.Equal("synthetic-save-truck/game.sii", request.Content.Save.GameSiiPath);
        Assert.Equal(Path.Combine(Path.GetFullPath("synthetic-home"), "mod"), Assert.Single(request.Content.ModDirectories!).DirectoryPath);
        Assert.Equal("synthetic-backup/game.sii", vm.SaveEditorLastBackupPath);
        Assert.True(service.Requests.Count >= 2);
        Assert.Contains("gespeichert", vm.SaveEditorStatusText);
    }

    [Fact]
    public async Task InstallCannotTrustForgedRowsOrMutableVerifiedFlag()
    {
        var (harness, _) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        var installer = new Installer();
        ConfigureInstaller(vm, installer, new ProcessGuard());
        SelectChangedPair(vm);
        var forged = new TruckPowertrainOptionViewModel(new(ScsPowertrainKind.Engine, Model + "engine/boost.sii", "Forged", false, "Fake", "", true, []));
        vm.TruckPowertrainEngines.Add(forged);
        vm.TruckPowertrainSelectedEngine = forged;
        vm.TruckPowertrainPairVerified = true;
        Assert.False(InstallCommand(vm).CanExecute(null));
        await InstallCommand(vm).ExecuteAsync(null);
        Assert.Empty(installer.Requests);
    }

    [Fact]
    public async Task InstallReassessesPairInsteadOfTrustingPreviouslyDisplayedResult()
    {
        Preview? preview = null;
        var (harness, _) = Create((request, _) => Task.FromResult<IScsTruckPowertrainPreview>(preview = new Preview(request)));
        var vm = harness.ViewModel;
        await Loaded(vm);
        var installer = new Installer();
        ConfigureInstaller(vm, installer, new ProcessGuard());
        SelectChangedPair(vm);
        Assert.True(vm.TruckPowertrainPairVerified);
        preview!.PairAllowed = false;
        Assert.False(InstallCommand(vm).CanExecute(null));
        await InstallCommand(vm).ExecuteAsync(null);
        Assert.Empty(installer.Requests);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("profile")]
    [InlineData("game")]
    [InlineData("truck")]
    [InlineData("modset")]
    [InlineData("installation")]
    [InlineData("engine")]
    [InlineData("transmission")]
    [InlineData("refresh")]
    public async Task InstallRejectsContextOrSelectionChangesDuringConfirmation(string change)
    {
        var (harness, _) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        var installer = new Installer();
        ConfigureInstaller(vm, installer, new ProcessGuard());
        SelectChangedPair(vm);
        var confirmations = 0;
        harness.Confirm = (_, _) =>
        {
            confirmations++;
            switch (change)
            {
                case "save": vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("new"); break;
                case "profile": vm.SaveEditorSelectedProfile = null; break;
                case "game": vm.SaveEditorSelectedGame = GameType.Ats; break;
                case "truck": vm.SaveEditorSelectedTruck = new("other", 0, false, "Other truck"); break;
                case "modset": vm.SelectedModset = Modset(GameType.Ets2, "changed"); break;
                case "installation": typeof(MainViewModel).GetMethod("SetSavedPath", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(vm, [GameType.Ets2, Path.GetFullPath("changed-install")]); break;
                case "engine": vm.TruckPowertrainSelectedEngine = vm.TruckPowertrainEngines.First(item => item.IsCurrent); break;
                case "transmission": vm.TruckPowertrainSelectedTransmission = vm.TruckPowertrainTransmissions.First(item => item.IsCurrent); break;
                case "refresh": vm.RefreshTruckPowertrainCommand.Execute(null); break;
            }
            return true;
        };
        await InstallCommand(vm).ExecuteAsync(null);
        Assert.Equal(1, confirmations);
        Assert.Empty(installer.Requests);
        Assert.Contains("Bestätigung", vm.SaveEditorStatusText);
    }

    [Theory]
    [InlineData(ScsSaveKind.Manual)]
    [InlineData(ScsSaveKind.AutoSave)]
    [InlineData(ScsSaveKind.QuickSave)]
    public async Task InstallRequiresBothGamesStoppedRegardlessOfSaveKind(ScsSaveKind kind)
    {
        var (harness, _) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        vm.SaveEditorSelectedSave = vm.SaveEditorSelectedSave! with { Kind = kind };
        await Loaded(vm);
        var installer = new Installer();
        var guard = new ProcessGuard { Running = true };
        ConfigureInstaller(vm, installer, guard);
        SelectChangedPair(vm);
        var confirmations = 0;
        harness.Confirm = (_, _) => { confirmations++; return true; };
        Assert.False(InstallCommand(vm).CanExecute(null));
        await InstallCommand(vm).ExecuteAsync(null);
        Assert.Empty(installer.Requests);
        Assert.Equal(0, confirmations);
        Assert.Contains("beendet", vm.SaveEditorStatusText);
        Assert.True(guard.Checks > 0);
    }

    [Fact]
    public async Task InstallChecksGameProcessesAgainAfterConfirmation()
    {
        var (harness, _) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        var installer = new Installer();
        var guard = new ProcessGuard();
        ConfigureInstaller(vm, installer, guard);
        SelectChangedPair(vm);
        harness.Confirm = (_, _) => { guard.Running = true; return true; };
        await InstallCommand(vm).ExecuteAsync(null);
        Assert.Empty(installer.Requests);
        Assert.Contains("beendet", vm.SaveEditorStatusText);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("profile")]
    [InlineData("game")]
    [InlineData("truck")]
    [InlineData("modset")]
    [InlineData("installation")]
    public async Task InstallCancelsBackendIfContextChangesDuringFreshVerification(string change)
    {
        var (harness, _) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        var installer = new Installer();
        ConfigureInstaller(vm, installer, new ProcessGuard());
        SelectChangedPair(vm);
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        installer.AsyncHandler = async (request, token) =>
        {
            started.SetResult(token);
            await finish.Task;
            token.ThrowIfCancellationRequested();
            return Installed(request);
        };
        var operation = InstallCommand(vm).ExecuteAsync(null);
        var cancellation = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(cancellation.IsCancellationRequested); // IsBusy cleared only the preview.
        switch (change)
        {
            case "save": vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("new"); break;
            case "profile": vm.SaveEditorSelectedProfile = null; break;
            case "game": vm.SaveEditorSelectedGame = GameType.Ats; break;
            case "truck": vm.SaveEditorSelectedTruck = new("other", 0, false, "Other"); break;
            case "modset": vm.SelectedModset = Modset(GameType.Ets2, "changed"); break;
            case "installation": typeof(MainViewModel).GetMethod("SetSavedPath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(vm, [GameType.Ets2, Path.GetFullPath("changed-install")]); break;
        }
        var wasCanceled = cancellation.IsCancellationRequested;
        finish.SetResult();
        await operation;
        Assert.True(wasCanceled);
        Assert.False(vm.IsBusy);
        Assert.NotEqual("synthetic-backup/game.sii", vm.SaveEditorLastBackupPath);
        Assert.DoesNotContain("erfolgreich gespeichert", vm.SaveEditorStatusText);
    }

    [Fact]
    public async Task CompletedInstallKeepsOriginalBackupWithoutRefreshingAnotherSelectedSave()
    {
        var (harness, _) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        var installer = new Installer();
        ConfigureInstaller(vm, installer, new ProcessGuard());
        SelectChangedPair(vm);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        installer.AsyncHandler = async (request, _) =>
        {
            started.SetResult();
            await finish.Task;
            return Installed(request); // The write completed before cancellation could prevent it.
        };
        var operation = InstallCommand(vm).ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var otherSave = SaveEditorTestHarness.Save("other");
        vm.SaveEditorSelectedSave = otherSave;
        finish.SetResult();
        await operation;
        Assert.Same(otherSave, vm.SaveEditorSelectedSave);
        Assert.Equal("synthetic-backup/game.sii", vm.SaveEditorLastBackupPath);
        Assert.Contains("synthetic-save-truck/game.sii", vm.SaveEditorStatusText);
        Assert.Contains("gespeichert", vm.SaveEditorStatusText);
    }

    [Fact]
    public async Task InstallDoesNotWriteRemoteCloudProfiles()
    {
        var (harness, _) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        vm.SaveEditorSelectedProfile = vm.SaveEditorSelectedProfile! with { StorageKind = ScsProfileStorageKind.SteamCloudRemote };
        await Loaded(vm);
        var installer = new Installer();
        ConfigureInstaller(vm, installer, new ProcessGuard());
        SelectChangedPair(vm);
        Assert.False(InstallCommand(vm).CanExecute(null));
        await InstallCommand(vm).ExecuteAsync(null);
        Assert.Empty(installer.Requests);
        Assert.Contains("Cloud", vm.SaveEditorStatusText);
    }

    [Fact]
    public async Task ConfiguredInstallerBlocksBothLegacyCommandsIncludingDirectExecution()
    {
        var (harness, _) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        Assert.True(vm.SetSaveEditorEngineCommand.CanExecute(null));
        Assert.True(vm.SetSaveEditorTransmissionCommand.CanExecute(null));
        var legacyWrites = 0;
        harness.VehicleOperation = (_, _) => { legacyWrites++; throw new InvalidOperationException("Legacy write"); };
        ConfigureInstaller(vm, new Installer(), new ProcessGuard());
        Assert.False(vm.SetSaveEditorEngineCommand.CanExecute(null));
        Assert.False(vm.SetSaveEditorTransmissionCommand.CanExecute(null));
        await vm.SetSaveEditorEngineCommand.ExecuteAsync(null);
        await vm.SetSaveEditorTransmissionCommand.ExecuteAsync(null);
        Assert.Equal(0, legacyWrites);
    }

    [Fact]
    public async Task DeclinedInstallAndBackendFailureDoNotReportSuccess()
    {
        var (harness, _) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        var installer = new Installer();
        ConfigureInstaller(vm, installer, new ProcessGuard());
        SelectChangedPair(vm);
        harness.Confirm = (_, _) => false;
        await InstallCommand(vm).ExecuteAsync(null);
        Assert.Empty(installer.Requests);
        harness.Confirm = (_, _) => true;
        installer.Handler = _ => Task.FromException<ScsSaveEditResult>(new ScsSaveEditException("Changed evidence"));
        await InstallCommand(vm).ExecuteAsync(null);
        Assert.Equal("Changed evidence", vm.SaveEditorStatusText);
        Assert.False(vm.IsBusy);
        Assert.NotEqual("synthetic-backup/game.sii", vm.SaveEditorLastBackupPath);
    }

    [Fact]
    public Task ViewBindsOnePairInstallAction() => OnStaThread(async () =>
    {
        var (harness, _) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        ConfigureInstaller(vm, new Installer(), new ProcessGuard());
        var view = new TruckPowertrainSelection { DataContext = vm };
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        var button = Assert.IsType<Button>(view.FindName("InstallPowertrain"));
        Assert.Same(InstallCommand(vm), button.Command);
        Assert.False(button.IsEnabled);
        SelectChangedPair(vm);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert.True(button.IsEnabled);
    });

    private static void SelectChangedPair(MainViewModel vm)
    {
        vm.TruckPowertrainSelectedEngine = vm.TruckPowertrainEngines.Single(item => item.Name == "Boost engine");
        vm.TruckPowertrainSelectedTransmission = vm.TruckPowertrainTransmissions.Single(item => item.Name == "Sport transmission");
    }

    private static void ConfigureInstaller(MainViewModel vm, Installer installer, ProcessGuard guard)
        => vm.ConfigureTruckPowertrainInstaller(installer, guard);
    private static IAsyncRelayCommand InstallCommand(MainViewModel vm) => vm.InstallTruckPowertrainCommand;
    private static ScsSaveEditResult Installed(ScsTruckPowertrainInstallRequest request) => new(
        request.Content.Save.GameSiiPath, "synthetic-backup/game.sii", "Powertrain", request.ExpectedSaveSha256, new string('B', 64), DateTimeOffset.UtcNow);
    private sealed class Installer : IScsTruckPowertrainInstaller
    {
        public List<ScsTruckPowertrainInstallRequest> Requests { get; } = [];
        public Func<ScsTruckPowertrainInstallRequest, Task<ScsSaveEditResult>> Handler { get; set; } = request => Task.FromResult(Installed(request));
        public Func<ScsTruckPowertrainInstallRequest, CancellationToken, Task<ScsSaveEditResult>>? AsyncHandler { get; set; }
        public Task<ScsSaveEditResult> InstallAsync(ScsTruckPowertrainInstallRequest request, CancellationToken cancellationToken = default)
        { Requests.Add(request); return AsyncHandler?.Invoke(request, cancellationToken) ?? Handler(request); }
    }
    private sealed class ProcessGuard : IScsGameProcessGuard
    {
        public bool Running { get; set; }
        public int Checks { get; private set; }
        public bool IsAnyScsGameRunning() { Checks++; return Running; }
    }
}
