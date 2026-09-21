namespace NmcScsLauncher.Core;

public sealed record ScsActiveVehicleState(
    string TruckId,
    string? TrailerId,
    int TrailerUnits,
    decimal? FuelRelative);

public interface IScsVehicleEditService
{
    Task<ScsActiveVehicleState> InspectActiveVehiclesAsync(
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
}
