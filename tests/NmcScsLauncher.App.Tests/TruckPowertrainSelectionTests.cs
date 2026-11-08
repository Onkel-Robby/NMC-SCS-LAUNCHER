using System.Collections.Concurrent;
using System.IO;
using System.Windows.Controls;
using System.Windows.Threading;
using NmcScsLauncher.App.ViewModels;
using NmcScsLauncher.App.Views;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.App.Tests;

public sealed partial class TruckPowertrainSelectionTests
{
    private const string Model = "/def/vehicle/truck/test/";

    [Fact]
    public async Task AutomaticallyLoadsReadableOptionsAndAssessesTheSelectedPair()
    {
        var (harness, service) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        Assert.Single(service.Requests);
        Assert.Equal("Current engine", vm.TruckPowertrainSelectedEngine?.Name);
        Assert.True(vm.TruckPowertrainPairVerified);
        vm.TruckPowertrainSelectedEngine = vm.TruckPowertrainEngines.Single(item => item.Name == "Boost engine");
        Assert.False(vm.TruckPowertrainPairVerified);
        Assert.Contains("Partner", vm.TruckPowertrainPairStatus);
        vm.TruckPowertrainSelectedTransmission = vm.TruckPowertrainTransmissions.Single(item => item.Name == "Sport transmission");
        Assert.True(vm.TruckPowertrainPairVerified);
        Assert.Equal("Workshop source", vm.TruckPowertrainSelectedEngine.SourceName);
        Assert.Contains("700", vm.TruckPowertrainSelectedEngine.TechnicalInfo);
        Assert.DoesNotContain(vm.SaveEditorEngines, item => item.DataPath.EndsWith("boost.sii"));
    }

    [Fact]
    public async Task SearchAndUnsupportedReasonsAreAvailableWithoutAnInstallAction()
    {
        var (harness, _) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        var unknown = vm.TruckPowertrainEngines.Single(item => item.Name == "Unknown engine");
        Assert.False(unknown.IsSelectable);
        Assert.Contains("DLC", unknown.ReasonText);
        vm.TruckPowertrainEngineSearch = "700";
        Assert.Equal("Boost engine", Assert.Single(vm.TruckPowertrainEngines).Name);
        Assert.Equal(2, vm.TruckPowertrainTransmissions.Count);
        vm.TruckPowertrainTransmissionSearch = "sport";
        Assert.Equal("Sport transmission", Assert.Single(vm.TruckPowertrainTransmissions).Name);
        Assert.Contains("Vorschau", vm.TruckPowertrainStatus);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("profile")]
    [InlineData("game")]
    [InlineData("truck")]
    [InlineData("busy")]
    [InlineData("modset")]
    [InlineData("installation")]
    public async Task DelayedPreviewCannotPublishAfterContextChanged(string change)
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<IScsTruckPowertrainPreview>(TaskCreationOptions.RunContinuationsAsynchronously);
        ScsEffectiveContentRequest? captured = null;
        var (harness, service) = Create((request, token) => { captured = request; entered.TrySetResult(token); return pending.Task; });
        var vm = harness.ViewModel;
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        service.Handler = (request, _) => Task.FromResult<IScsTruckPowertrainPreview>(new Preview(request, "New result"));
        switch (change)
        {
            case "save": vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("new"); break;
            case "profile": vm.SaveEditorSelectedProfile = null; break;
            case "game": vm.SaveEditorSelectedGame = GameType.Ats; break;
            case "truck": vm.SaveEditorSelectedTruck = new("other", 0, false, "Other truck"); break;
            case "busy": vm.IsBusy = true; break;
            case "modset": vm.SelectedModset = Modset(GameType.Ets2, "unrelated"); break;
            case "installation":
                typeof(MainViewModel).GetMethod("SetSavedPath", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                    .Invoke(vm, [GameType.Ets2, Path.GetFullPath("new-install")]);
                break;
        }
        pending.SetResult(new Preview(captured!, "Stale result"));
        await Task.Delay(100);
        Assert.True(token.IsCancellationRequested);
        Assert.DoesNotContain(vm.TruckPowertrainEngines, item => item.Name == "Stale result");
        Assert.DoesNotContain("old diagnostic", vm.TruckPowertrainDiagnostics);
        if (change == "installation")
            Assert.Equal(Path.GetFullPath("new-install"), service.Requests.Last().PreferredInstallPath);
    }

    [Fact]
    public async Task BusyAndInspectionCompletionResumeAutomaticLoading()
    {
        var harness = Harness();
        harness.ViewModel.IsBusy = true;
        var service = new Service();
        harness.ViewModel.ConfigureTruckPowertrainService(service);
        Assert.Empty(service.Requests);
        harness.ViewModel.IsBusy = false;
        await Loaded(harness.ViewModel);
        Assert.Single(service.Requests);
    }

    [Fact]
    public async Task CatalogLoadedDuringInspectionStartsPreviewWhenInspectionFinishes()
    {
        var service = new Service();
        var harness = Harness(service: service);
        await Loaded(harness.ViewModel);
        Assert.Single(service.Requests);
    }

    [Fact]
    public async Task PreviewForAnotherSaveCannotBeDisplayed()
    {
        var (harness, _) = Create((request, _) => Task.FromResult<IScsTruckPowertrainPreview>(new Preview(request with
        { Save = request.Save with { GameSiiPath = "another-save/game.sii" } })));
        for (var count = 0; count < 500 && harness.ViewModel.TruckPowertrainIsLoading; count++) await Task.Delay(10);
        Assert.Empty(harness.ViewModel.TruckPowertrainEngines);
        Assert.Contains("ausgewählten Truck", harness.ViewModel.TruckPowertrainDiagnostics);
        Assert.False(harness.ViewModel.TruckPowertrainPairVerified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsesOnlyMatchingSelectedLaunchModset(bool direct)
    {
        var selected = Modset(GameType.Ets2, "chosen") with { ModDirectoryPath = direct ? Path.GetFullPath("chosen-mods") : null };
        var expectedHome = direct ? ScsModsetPathResolver.GetStandardGameDataDirectory(GameType.Ets2) : ScsModsetPathResolver.GetRuntimeGameDataDirectory(selected);
        var harness = Harness(expectedHome);
        harness.ViewModel.SelectedModset = selected;
        harness.ViewModel.Modsets.Add(Modset(GameType.Ets2, "other"));
        harness.ViewModel.Modsets.Add(Modset(GameType.Ats, "ats"));
        var service = new Service();
        harness.ViewModel.ConfigureTruckPowertrainService(service);
        await Loaded(harness.ViewModel);
        var root = Assert.Single(Assert.Single(service.Requests).ModDirectories!);
        Assert.Equal(ScsModsetPathResolver.GetModDirectory(selected), root.DirectoryPath);
        Assert.Contains("chosen", harness.ViewModel.TruckPowertrainContext);
    }

    [Theory]
    [InlineData(GameType.Ets2)]
    [InlineData(GameType.Ats)]
    public async Task MismatchingModsetFallsBackToSelectedProfileHome(GameType modsetGame)
    {
        var harness = Harness();
        harness.ViewModel.SelectedModset = Modset(modsetGame, "unrelated");
        var service = new Service();
        harness.ViewModel.ConfigureTruckPowertrainService(service);
        await Loaded(harness.ViewModel);
        var root = Assert.Single(Assert.Single(service.Requests).ModDirectories!);
        Assert.Equal(Path.Combine(Path.GetFullPath("synthetic-home"), "mod"), root.DirectoryPath);
        Assert.DoesNotContain("unrelated", harness.ViewModel.TruckPowertrainContext);
    }

    [Fact]
    public async Task ForgedUiSelectionCannotBecomeAnAssessedPair()
    {
        var (harness, _) = Create();
        var vm = harness.ViewModel;
        await Loaded(vm);
        var forged = new TruckPowertrainOptionViewModel(new(ScsPowertrainKind.Engine, Model + "engine/forged.sii", "Forged", false, "Fake", "", true, []));
        vm.TruckPowertrainEngines.Add(forged);
        vm.TruckPowertrainSelectedEngine = forged;
        Assert.False(vm.TruckPowertrainPairVerified);
    }

    [Fact]
    public Task ViewBindsSearchAndBothSelections() => OnStaThread(async () =>
    {
        var (harness, _) = Create();
        await Loaded(harness.ViewModel);
        var view = new TruckPowertrainSelection { DataContext = harness.ViewModel };
        var engineSearch = Assert.IsType<TextBox>(view.FindName("EngineSearch"));
        var engines = Assert.IsType<ListBox>(view.FindName("EngineOptions"));
        var transmissions = Assert.IsType<ListBox>(view.FindName("TransmissionOptions"));
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        view.Measure(new System.Windows.Size(900, 700));
        view.Arrange(new System.Windows.Rect(0, 0, 900, 700));
        view.UpdateLayout();
        Assert.Same(harness.ViewModel.TruckPowertrainEngines, engines.ItemsSource);
        Assert.Same(harness.ViewModel.TruckPowertrainTransmissions, transmissions.ItemsSource);
        var unknown = harness.ViewModel.TruckPowertrainEngines.Single(item => !item.IsSelectable);
        Assert.False(Assert.IsType<ListBoxItem>(engines.ItemContainerGenerator.ContainerFromItem(unknown)).IsEnabled);
        var boost = harness.ViewModel.TruckPowertrainEngines.Single(item => item.Name == "Boost engine");
        engines.SetCurrentValue(ListBox.SelectedItemProperty, boost);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert.Same(boost, harness.ViewModel.TruckPowertrainSelectedEngine);
        Assert.False(harness.ViewModel.TruckPowertrainPairVerified);
        engineSearch.SetCurrentValue(TextBox.TextProperty, "Boost");
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert.Equal("Boost", harness.ViewModel.TruckPowertrainEngineSearch);
    });

    private static SaveEditorTestHarness Harness(string home = "synthetic-home", Service? service = null)
    {
        var harness = new SaveEditorTestHarness();
        harness.Powertrain = _ => Task.FromResult(new ScsActiveTruckPowertrain(Model + "engine/current.sii", Model + "transmission/current.sii"));
        harness.PowertrainCatalog = _ => Task.FromResult(new ScsActiveTruckPowertrainCatalog(Model,
            [new(Model + "engine/current.sii", "truck", true)], [new(Model + "transmission/current.sii", "truck", true)]));
        harness.FindSaves = _ => Task.FromResult<IReadOnlyList<ScsSaveReference>>([SaveEditorTestHarness.Save("truck")]);
        if (service is not null) harness.ViewModel.ConfigureTruckPowertrainService(service);
        harness.ViewModel.SaveEditorSelectedProfile = SaveEditorTestHarness.Profile("truck") with { GameHomeDirectory = home };
        return harness;
    }
    private static (SaveEditorTestHarness, Service) Create(Func<ScsEffectiveContentRequest, CancellationToken, Task<IScsTruckPowertrainPreview>>? handler = null)
    {
        var harness = Harness();
        var service = new Service();
        if (handler is not null) service.Handler = handler;
        harness.ViewModel.ConfigureTruckPowertrainService(service);
        return (harness, service);
    }
    private static async Task Loaded(MainViewModel vm)
    {
        for (var count = 0; count < 500 && (vm.TruckPowertrainIsLoading || vm.TruckPowertrainEngines.Count == 0); count++) await Task.Delay(10);
        Assert.NotEmpty(vm.TruckPowertrainEngines);
    }
    private static Modset Modset(GameType game, string name) => new()
    { Id = Guid.NewGuid(), Game = game, Name = name, HomeBasePath = Path.GetFullPath(name), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, IsManagedDirectory = true };
    private sealed class Service : IScsTruckPowertrainService
    {
        public ConcurrentQueue<ScsEffectiveContentRequest> Requests { get; } = new();
        public Func<ScsEffectiveContentRequest, CancellationToken, Task<IScsTruckPowertrainPreview>> Handler { get; set; } = (request, _) => Task.FromResult<IScsTruckPowertrainPreview>(new Preview(request));
        public Task<IScsTruckPowertrainPreview> InspectAsync(ScsEffectiveContentRequest request, CancellationToken cancellationToken = default)
        { Requests.Enqueue(request); return Handler(request, cancellationToken); }
    }
    private sealed class Preview : IScsTruckPowertrainPreview
    {
        public bool PairAllowed { get; set; } = true;
        public ScsTruckPowertrainInspectionReport? InspectionReport { get; set; }
        public Preview(ScsEffectiveContentRequest request, string currentName = "Current engine",
            IReadOnlyList<ScsSaveUnitReadDiagnostic>? unitReadDiagnostics = null)
        {
            Snapshot = new(request.Profile.GameType, request.Save.GameSiiPath, new string('A', 64), request.Save.DisplayName, Model, "data", "engine", "transmission", Model + "engine/current.sii", Model + "transmission/current.sii", [], UnitReadDiagnostics: unitReadDiagnostics);
            Engines = [new(ScsPowertrainKind.Engine, Snapshot.CurrentEnginePath, currentName, true, "Game", "420 hp", true, []),
                new(ScsPowertrainKind.Engine, Model + "engine/boost.sii", "Boost engine", false, "Workshop source", "700 hp", true, []),
                new(ScsPowertrainKind.Engine, Model + "engine/unknown.sii", "Unknown engine", false, "DLC", "", false, ["DLC-Berechtigung ungeklärt"])];
            Transmissions = [new(ScsPowertrainKind.Transmission, Snapshot.CurrentTransmissionPath, "Current transmission", true, "Game", "12 Gänge", true, []),
                new(ScsPowertrainKind.Transmission, Model + "transmission/sport.sii", "Sport transmission", false, "Workshop", "18 Gänge", true, [])];
        }
        public ScsTruckPowertrainSnapshot Snapshot { get; }
        public IReadOnlyList<ScsTruckPowertrainOption> Engines { get; }
        public IReadOnlyList<ScsTruckPowertrainOption> Transmissions { get; }
        public IReadOnlyList<string> Diagnostics => [];
        public ScsTruckPowertrainAssessment AssessPair(string engineDataPath, string transmissionDataPath)
        {
            var verified = PairAllowed && engineDataPath.Contains("boost") == transmissionDataPath.Contains("sport");
            return new(engineDataPath, transmissionDataPath, verified, verified ? [] : ["Partner muss ebenfalls gewechselt werden."]);
        }
    }
    private static Task OnStaThread(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.InvokeAsync(async () =>
            {
                try { await action(); completion.SetResult(); }
                catch (Exception exception) { completion.SetException(exception); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
