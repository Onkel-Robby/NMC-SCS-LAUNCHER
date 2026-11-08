using System.IO;
using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.App.Tests;

public sealed class SaveEditorPowertrainDiscoveryTests
{
    private const string ModelRoot = "/def/vehicle/truck/volvo.fh16/";
    private static readonly ScsPowertrainCatalogItem CurrentEngine = new(ModelRoot + "engine/current.sii", "truck", true);
    private static readonly ScsPowertrainCatalogItem OtherEngine = new(ModelRoot + "engine/other.sii", "donor", false);
    private static readonly ScsPowertrainCatalogItem CurrentTransmission = new(ModelRoot + "transmission/current.sii", "truck", true);
    private static readonly ScsPowertrainCatalogItem OtherTransmission = new(ModelRoot + "transmission/other.sii", "donor", false);

    [Theory]
    [InlineData(GameType.Ets2)]
    [InlineData(GameType.Ats)]
    public async Task DiscoveryUsesSelectedProfileAndGameWithoutAddingExternalWriteChoices(GameType game)
    {
        var harness = CreateHarness(game);
        var vm = harness.ViewModel;
        var profile = SaveEditorTestHarness.Profile("selected", game) with { GameHomeDirectory = "selected-profile-home" };
        harness.FindSaves = _ => Task.FromResult<IReadOnlyList<ScsSaveReference>>([SaveEditorTestHarness.Save("selected", game)]);
        vm.SaveEditorSelectedProfile = profile;
        ScsPowertrainDiscoveryRequest? captured = null;
        var external = External(game);
        harness.DiscoverPowertrain = (request, _) =>
        {
            captured = request;
            return Task.FromResult(new ScsPowertrainDiscoveryResult([external], []));
        };

        await vm.DiscoverSaveEditorPowertrainCommand.ExecuteAsync(null);

        Assert.NotNull(captured);
        Assert.Equal(game, captured.Game);
        Assert.Equal("selected-profile-home", captured.GameHomeDirectory);
        Assert.Equal(ModelRoot, captured.SaveCatalog.TruckModelRoot);
        Assert.Single(vm.SaveEditorDiscoveredPowertrains);
        Assert.Equal("Nicht verifiziert", vm.SaveEditorDiscoveredPowertrains[0].CompatibilityText);
        Assert.Equal(new[] { CurrentEngine, OtherEngine }, vm.SaveEditorEngines);
        Assert.DoesNotContain(vm.SaveEditorEngines, item => item.DataPath == external.DataPath);
        Assert.Same(CurrentEngine, vm.SaveEditorSelectedEngine);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("profile")]
    [InlineData("game")]
    public async Task DelayedDiscoveryCannotPublishAfterContextChanged(string change)
    {
        var harness = CreateHarness();
        var vm = harness.ViewModel;
        var entered = new TaskCompletionSource<CancellationToken>();
        var pending = new TaskCompletionSource<ScsPowertrainDiscoveryResult>();
        harness.DiscoverPowertrain = (_, token) => { entered.SetResult(token); return pending.Task; };
        var scan = vm.DiscoverSaveEditorPowertrainCommand.ExecuteAsync(null);
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        switch (change)
        {
            case "save": vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("new"); break;
            case "profile": vm.SaveEditorSelectedProfile = SaveEditorTestHarness.Profile("new"); break;
            case "game": vm.SaveEditorSelectedGame = GameType.Ats; break;
        }
        pending.SetResult(new([External()], ["old diagnostic"]));
        await scan;

        Assert.True(token.IsCancellationRequested);
        Assert.Empty(vm.SaveEditorDiscoveredPowertrains);
        Assert.DoesNotContain("old diagnostic", vm.SaveEditorPowertrainDiagnostics);
        Assert.False(vm.SaveEditorIsDiscoveringPowertrain);
    }

    [Fact]
    public async Task CancellationDiscardsResultEvenIfReaderIgnoresToken()
    {
        var harness = CreateHarness();
        var vm = harness.ViewModel;
        var entered = new TaskCompletionSource();
        var pending = new TaskCompletionSource<ScsPowertrainDiscoveryResult>();
        harness.DiscoverPowertrain = (_, _) => { entered.SetResult(); return pending.Task; };
        var scan = vm.DiscoverSaveEditorPowertrainCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(vm.SaveEditorCanEditPlates);
        Assert.True(vm.CancelSaveEditorPowertrainDiscoveryCommand.CanExecute(null));

        vm.CancelSaveEditorPowertrainDiscoveryCommand.Execute(null);
        pending.SetResult(new([External()], []));
        await scan;

        Assert.Empty(vm.SaveEditorDiscoveredPowertrains);
        Assert.Contains("abgebrochen", vm.SaveEditorPowertrainStatusText);
        Assert.True(vm.DiscoverSaveEditorPowertrainCommand.CanExecute(null));
    }

    [Fact]
    public async Task FailedDiscoveryPreservesWritableSaveCatalog()
    {
        var harness = CreateHarness();
        var vm = harness.ViewModel;
        harness.DiscoverPowertrain = (_, _) => throw new IOException("synthetic inaccessible archive");

        await vm.DiscoverSaveEditorPowertrainCommand.ExecuteAsync(null);

        Assert.Contains("nicht gelesen", vm.SaveEditorPowertrainStatusText);
        Assert.Equal(2, vm.SaveEditorEngines.Count);
        Assert.Equal(2, vm.SaveEditorTransmissions.Count);
        Assert.True(vm.SetSaveEditorEngineCommand.CanExecute(null));
        Assert.True(vm.SaveEditorCanEditPlates);
    }

    [Fact]
    public async Task DiscoverySnapshotsOnlyTheSelectedGamesLauncherModFolders()
    {
        var harness = CreateHarness();
        var vm = harness.ViewModel;
        var etsModset = Modset(GameType.Ets2, "ETS folder", "synthetic-ets-mods");
        var atsModset = Modset(GameType.Ats, "ATS folder", "synthetic-ats-mods");
        vm.Modsets.Add(etsModset);
        vm.Modsets.Add(atsModset);
        vm.SelectedModset = atsModset;
        ScsPowertrainDiscoveryRequest? captured = null;
        harness.DiscoverPowertrain = (request, _) =>
        {
            captured = request;
            return Task.FromResult(new ScsPowertrainDiscoveryResult([], []));
        };

        await vm.DiscoverSaveEditorPowertrainCommand.ExecuteAsync(null);

        var folder = Assert.Single(captured!.ModDirectories!);
        Assert.Equal(GameType.Ets2, folder.Game);
        Assert.Equal("ETS folder", folder.Name);
        Assert.Equal(Path.GetFullPath("synthetic-ets-mods"), folder.DirectoryPath);
    }

    [Fact]
    public async Task OldScanCannotReplaceNewScanOrResetItsLoadingState()
    {
        var harness = CreateHarness();
        var vm = harness.ViewModel;
        var entered = new TaskCompletionSource();
        var oldResult = new TaskCompletionSource<ScsPowertrainDiscoveryResult>();
        harness.DiscoverPowertrain = (_, _) => { entered.SetResult(); return oldResult.Task; };
        var oldScan = vm.DiscoverSaveEditorPowertrainCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        vm.CancelSaveEditorPowertrainDiscoveryCommand.Execute(null);
        var nextEntered = new TaskCompletionSource();
        var nextResult = new TaskCompletionSource<ScsPowertrainDiscoveryResult>();
        harness.DiscoverPowertrain = (_, _) => { nextEntered.SetResult(); return nextResult.Task; };
        var nextScan = vm.DiscoverSaveEditorPowertrainCommand.ExecuteAsync(null);
        await nextEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        oldResult.SetResult(new([External() with { Name = "Old" }], []));
        await oldScan;
        Assert.True(vm.SaveEditorIsDiscoveringPowertrain);
        nextResult.SetResult(new([External() with { Name = "New" }], []));
        await nextScan;

        Assert.Equal("New", Assert.Single(vm.SaveEditorDiscoveredPowertrains).Name);
        Assert.False(vm.SaveEditorIsDiscoveringPowertrain);
    }

    [Fact]
    public async Task CommandsRemainDisabledDuringInspectionAndWrite()
    {
        var harness = CreateHarness();
        var vm = harness.ViewModel;
        vm.IsBusy = true;
        Assert.False(vm.SetSaveEditorEngineCommand.CanExecute(null));
        Assert.False(vm.SetSaveEditorTransmissionCommand.CanExecute(null));
        Assert.False(vm.DiscoverSaveEditorPowertrainCommand.CanExecute(null));
        vm.IsBusy = false;
        Assert.True(vm.SetSaveEditorEngineCommand.CanExecute(null));
        var pending = new TaskCompletionSource<ScsActiveVehicleState>();
        harness.Inspect = _ => pending.Task;
        vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("pending");
        Assert.False(vm.SetSaveEditorEngineCommand.CanExecute(null));
        Assert.False(vm.SetSaveEditorTransmissionCommand.CanExecute(null));
        Assert.False(vm.DiscoverSaveEditorPowertrainCommand.CanExecute(null));
        pending.SetResult(new("truck", null, 0, .5m));
        await Task.Yield();
    }

    [Fact]
    public async Task SearchMatchesNamesModelsPathsAndSourcesAndKeepsPreviewSeparate()
    {
        var harness = CreateHarness();
        var vm = harness.ViewModel;
        var foreign = External() with { TruckModel = "scania.r", DataPath = "/def/vehicle/truck/scania.r/engine/foreign.sii", Name = "Foreign", Compatibility = ScsPowertrainCompatibility.DifferentModel };
        harness.DiscoverPowertrain = (_, _) => Task.FromResult(new ScsPowertrainDiscoveryResult([External(), foreign], []));
        await vm.DiscoverSaveEditorPowertrainCommand.ExecuteAsync(null);
        Assert.Single(vm.SaveEditorDiscoveredPowertrains);
        vm.SaveEditorPowertrainOnlyCurrentModel = false;
        Assert.Equal(2, vm.SaveEditorDiscoveredPowertrains.Count);
        vm.SaveEditorPowertrainSearch = "foreign";
        Assert.Equal("scania.r", Assert.Single(vm.SaveEditorDiscoveredPowertrains).TruckModel);
        Assert.Same(CurrentEngine, vm.SaveEditorSelectedEngine);
        vm.SaveEditorPowertrainSearch = "synthetic dlc";
        Assert.Equal(2, vm.SaveEditorDiscoveredPowertrains.Count);
        vm.SaveEditorPowertrainSearch = "no match";
        Assert.Empty(vm.SaveEditorDiscoveredPowertrains);
        Assert.Null(vm.SaveEditorSelectedDiscoveredPowertrain);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalOrForgedSelectionCannotWriteEvenWhenAddedToVisibleChoices(bool transmission)
    {
        var harness = CreateHarness();
        var vm = harness.ViewModel;
        var forged = new ScsPowertrainCatalogItem(ModelRoot + (transmission ? "transmission" : "engine") + "/external.sii", "external", false);
        if (transmission) { vm.SaveEditorTransmissions.Add(forged); vm.SaveEditorSelectedTransmission = forged; }
        else { vm.SaveEditorEngines.Add(forged); vm.SaveEditorSelectedEngine = forged; }
        var confirmed = false;
        var written = false;
        harness.Confirm = (_, _) => { confirmed = true; return true; };
        harness.VehicleOperation = (_, _) => { written = true; return Task.FromException<ScsSaveEditResult>(new IOException()); };
        var command = transmission ? vm.SetSaveEditorTransmissionCommand : vm.SetSaveEditorEngineCommand;

        Assert.False(command.CanExecute(null));
        await command.ExecuteAsync(null);

        Assert.False(confirmed);
        Assert.False(written);
    }

    [Theory]
    [InlineData(false, "save")]
    [InlineData(true, "save")]
    [InlineData(false, "selection")]
    [InlineData(true, "selection")]
    [InlineData(false, "profile")]
    [InlineData(true, "game")]
    public async Task ChangedContextDuringConfirmationDoesNotWrite(bool transmission, string change)
    {
        var harness = CreateHarness();
        var vm = harness.ViewModel;
        var written = false;
        harness.VehicleOperation = (_, _) => { written = true; return Task.FromException<ScsSaveEditResult>(new IOException()); };
        harness.Confirm = (_, _) =>
        {
            switch (change)
            {
                case "save": vm.SaveEditorSelectedSave = SaveEditorTestHarness.Save("new"); break;
                case "profile": vm.SaveEditorSelectedProfile = SaveEditorTestHarness.Profile("new"); break;
                case "game": vm.SaveEditorSelectedGame = GameType.Ats; break;
                case "selection":
                    if (transmission) vm.SaveEditorSelectedTransmission = OtherTransmission;
                    else vm.SaveEditorSelectedEngine = OtherEngine;
                    break;
            }
            return true;
        };

        await (transmission ? vm.SetSaveEditorTransmissionCommand : vm.SetSaveEditorEngineCommand).ExecuteAsync(null);

        Assert.False(written);
        Assert.Contains("Bestätigung", vm.SaveEditorStatusText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingSaveChoiceUsesCapturedSaveAndValidatedOperation(bool transmission)
    {
        var harness = CreateHarness();
        var vm = harness.ViewModel;
        var save = vm.SaveEditorSelectedSave;
        if (transmission) vm.SaveEditorSelectedTransmission = OtherTransmission;
        else vm.SaveEditorSelectedEngine = OtherEngine;
        string? operation = null;
        object?[]? arguments = null;
        harness.VehicleOperation = (method, args) =>
        {
            operation = method;
            arguments = args;
            return Task.FromException<ScsSaveEditResult>(new ScsSaveEditException("Synthetic write boundary"));
        };

        await (transmission ? vm.SetSaveEditorTransmissionCommand : vm.SetSaveEditorEngineCommand).ExecuteAsync(null);

        Assert.Equal(transmission ? nameof(IScsVehicleEditService.SetActiveTruckTransmissionAsync) : nameof(IScsVehicleEditService.SetActiveTruckEngineAsync), operation);
        Assert.Same(save, arguments![0]);
        Assert.Equal(transmission ? OtherTransmission.DataPath : OtherEngine.DataPath, arguments[1]);
    }

    private static SaveEditorTestHarness CreateHarness(GameType game = GameType.Ets2)
    {
        var harness = new SaveEditorTestHarness(powertrainEnabled: true)
        {
            Powertrain = _ => Task.FromResult(new ScsActiveTruckPowertrain(CurrentEngine.DataPath, CurrentTransmission.DataPath)),
            PowertrainCatalog = _ => Task.FromResult(new ScsActiveTruckPowertrainCatalog(ModelRoot, [CurrentEngine, OtherEngine], [CurrentTransmission, OtherTransmission]))
        };
        harness.ViewModel.SaveEditorSelectedGame = game;
        harness.ViewModel.SaveEditorSelectedSave = SaveEditorTestHarness.Save("current", game);
        return harness;
    }

    private static ScsDiscoveredPowertrain External(GameType game = GameType.Ets2) => new(
        game, ScsPowertrainKind.Engine, ModelRoot + "engine/external.sii", "External Engine", "volvo.fh16", false,
        ScsPowertrainCompatibility.ModelMatchUnverified, "Aktivierung und Zubehörregeln nicht verifiziert.",
        [new(ScsPowertrainSourceKind.Dlc, "Synthetic DLC", "synthetic/dlc.scs")]);

    private static Modset Modset(GameType game, string name, string directory) => new()
    {
        Id = Guid.NewGuid(), Game = game, Name = name, HomeBasePath = "synthetic-home",
        ModDirectoryPath = directory, CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch, IsManagedDirectory = false
    };
}
