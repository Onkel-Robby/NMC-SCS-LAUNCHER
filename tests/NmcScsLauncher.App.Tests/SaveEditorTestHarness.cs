using System.Reflection;
using NmcScsLauncher.App.ViewModels;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.Tests;

internal sealed class SaveEditorTestHarness
{
    public Func<ScsProfileReference, Task<IReadOnlyList<ScsSaveReference>>> FindSaves { get; set; } =
        _ => Task.FromResult<IReadOnlyList<ScsSaveReference>>([]);
    public Func<ScsSaveReference, Task<ScsActiveVehicleState>> Inspect { get; set; } =
        save => Task.FromResult(new ScsActiveVehicleState(save.DisplayName, null, 0, 0.5m));
    public Func<GameType, Task<IReadOnlyList<ScsProfileReference>>> FindCloudProfiles { get; set; } =
        _ => Task.FromResult<IReadOnlyList<ScsProfileReference>>([]);
    public Func<string, object?[], object?>? VehicleOperation { get; set; }
    public Func<ScsSaveReference, Task<ScsActiveTruckPowertrain>> Powertrain { get; set; } =
        _ => Task.FromException<ScsActiveTruckPowertrain>(new ScsSaveEditException("No powertrain in fixture"));
    public Func<ScsSaveReference, Task<ScsActiveTruckPowertrainCatalog>> PowertrainCatalog { get; set; } =
        _ => Task.FromException<ScsActiveTruckPowertrainCatalog>(new ScsSaveEditException("No powertrain catalog in fixture"));
    public Func<ScsPowertrainDiscoveryRequest, CancellationToken, Task<ScsPowertrainDiscoveryResult>> DiscoverPowertrain { get; set; } =
        (_, _) => Task.FromResult(new ScsPowertrainDiscoveryResult([], []));
    public Func<string, string, bool> Confirm { get; set; } = (_, _) => true;
    public MainViewModel ViewModel { get; }

    public SaveEditorTestHarness()
    {
        var constructor = typeof(MainViewModel).GetConstructors().Single();
        ViewModel = (MainViewModel)constructor.Invoke(constructor.GetParameters()
            .Select(parameter => Proxy(parameter.ParameterType, (method, args) =>
                method.Name == "ConfirmSaveEdit" ? Confirm((string)args[0]!, (string)args[1]!) :
                method.ReturnType == typeof(Task) ? Task.CompletedTask :
                method.ReturnType == typeof(bool) ? true :
                throw new InvalidOperationException($"Unexpected dependency call: {method.Name}")))
            .ToArray());
        ViewModel.ConfigureSaveEditorServices(
            Proxy<IScsProfileSaveLocator>((method, args) => method.Name switch
            {
                nameof(IScsProfileSaveLocator.FindSavesAsync) => FindSaves((ScsProfileReference)args[0]!),
                nameof(IScsProfileSaveLocator.FindSteamCloudProfilesAsync) => FindCloudProfiles((GameType)args[0]!),
                _ => Task.FromResult<IReadOnlyList<ScsProfileReference>>([])
            }),
            Proxy<IScsProfileEditService>((method, _) => method.Name switch
            {
                nameof(IScsProfileEditService.GetMoneyAsync) => Task.FromResult(100L),
                nameof(IScsProfileEditService.GetExperienceAsync) => Task.FromResult(200L),
                nameof(IScsProfileEditService.GetCareerSkillsAsync) => Task.FromResult(new ScsCareerSkills(1, 2, 3, 4, 5, 6)),
                _ => throw new InvalidOperationException(method.Name)
            }),
            Proxy<IScsVehicleEditService>((method, args) =>
            {
                var save = (ScsSaveReference)args[0]!;
                return method.Name switch
                {
                    nameof(IScsVehicleEditService.InspectActiveVehiclesAsync) => Inspect(save),
                    nameof(IScsVehicleEditService.GetVehicleInventoryAsync) => Task.FromResult(new ScsVehicleInventory(
                        save.DisplayName, null, [new(save.DisplayName, 0, true, save.DisplayName)], [])),
                    nameof(IScsVehicleEditService.GetActiveTruckMileageAsync) => Task.FromResult(12m),
                    nameof(IScsVehicleEditService.GetActiveTruckPowertrainAsync) =>
                        Powertrain(save),
                    nameof(IScsVehicleEditService.GetActiveTruckPowertrainCatalogAsync) => PowertrainCatalog(save),
                    _ => VehicleOperation?.Invoke(method.Name, args)
                        ?? throw new InvalidOperationException(method.Name)
                };
            }),
            Proxy<IScsSaveEditService>((method, _) => throw new InvalidOperationException(method.Name)));
        ViewModel.ConfigurePowertrainDiscoveryService(
            Proxy<IScsPowertrainDiscoveryService>((_, args) =>
                DiscoverPowertrain((ScsPowertrainDiscoveryRequest)args[0]!, (CancellationToken)args[1]!)));
    }

    public static ScsProfileReference Profile(string name, GameType game = GameType.Ets2) =>
        new(game, ScsProfileStorageKind.Local, "synthetic-home", $"synthetic-profile-{name}", name, name, $"synthetic-profile-{name}/profile.sii");

    public static ScsSaveReference Save(string name, GameType game = GameType.Ets2) =>
        new(game, "synthetic-profile", $"synthetic-save-{name}", name, name, ScsSaveKind.Manual,
            $"synthetic-save-{name}/game.sii", DateTimeOffset.UnixEpoch);

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> handler) where T : class =>
        (T)Proxy(typeof(T), handler);

    private static object Proxy(Type contract, Func<MethodInfo, object?[], object?> handler)
    {
        var proxy = DispatchProxy.Create(contract, typeof(TestServiceProxy));
        ((TestServiceProxy)proxy).Handler = handler;
        return proxy;
    }
}

public class TestServiceProxy : DispatchProxy
{
    public Func<MethodInfo, object?[], object?> Handler { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        Handler(targetMethod!, args ?? []);
}
