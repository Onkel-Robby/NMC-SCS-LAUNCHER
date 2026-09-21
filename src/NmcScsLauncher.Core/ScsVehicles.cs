namespace NmcScsLauncher.Core;

public sealed record ScsActiveVehicleState(
    string TruckId,
    string? TrailerId,
    int TrailerUnits,
    decimal? FuelRelative);

public sealed record ScsVehicleInventoryItem(
    string Id,
    int Slot,
    bool IsActive);

public sealed record ScsVehicleInventory(
    string ActiveTruckId,
    string? ActiveTrailerId,
    IReadOnlyList<ScsVehicleInventoryItem> Trucks,
    IReadOnlyList<ScsVehicleInventoryItem> Trailers);

public interface IScsVehicleEditService
{
    Task<ScsActiveVehicleState> InspectActiveVehiclesAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default);

    Task<ScsVehicleInventory> GetVehicleInventoryAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default);

    Task<ScsSaveEditResult> RepairActiveTruckAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default);

    Task<ScsSaveEditResult> SetActiveTruckFuelAsync(
        ScsSaveReference save,
        decimal fuelRelative,
        CancellationToken cancellationToken = default);

    Task<ScsSaveEditResult> RepairActiveTrailerAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default);

    Task<ScsSaveEditResult> SwitchActiveTrailerAsync(
        ScsSaveReference save,
        string targetTrailerId,
        CancellationToken cancellationToken = default);
}
