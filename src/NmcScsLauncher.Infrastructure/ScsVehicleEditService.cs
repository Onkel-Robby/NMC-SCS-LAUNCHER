using System.Globalization;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsVehicleEditService : IScsVehicleEditService
{
    private static readonly HashSet<string> TruckWearKeys = new(StringComparer.Ordinal)
    {
        "engine_wear",
        "transmission_wear",
        "cabin_wear",
        "engine_wear_unfixable",
        "transmission_wear_unfixable",
        "cabin_wear_unfixable",
        "chassis_wear",
        "chassis_wear_unfixable"
    };

    private static readonly HashSet<string> TruckWheelWearPrefixes = new(StringComparer.Ordinal)
    {
        "wheels_wear",
        "wheels_wear_unfixable"
    };

    private static readonly HashSet<string> TrailerWearKeys = new(StringComparer.Ordinal)
    {
        "trailer_body_wear",
        "trailer_body_wear_unfixable",
        "chassis_wear",
        "chassis_wear_unfixable"
    };

    private static readonly HashSet<string> TrailerWheelWearPrefixes = new(StringComparer.Ordinal)
    {
        "wheels_wear",
        "wheels_wear_unfixable"
    };

    private readonly IScsSaveCodec _codec;
    private readonly IScsSaveEditService _saveEditService;

    public ScsVehicleEditService(
        IScsSaveCodec codec,
        IScsSaveEditService saveEditService)
    {
        _codec = codec;
        _saveEditService = saveEditService;
    }

    public async Task<ScsActiveVehicleState> InspectActiveVehiclesAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default)
    {
        ValidateSaveReference(save);

        var source = await _codec.ReadAsync(save.GameSiiPath, cancellationToken);
        var document = ScsSiiUnitDocument.Parse(source.Content);
        var refs = ResolveActiveReferences(document);

        decimal? fuel = null;
        var truckUnit = document.GetRequiredUniqueUnit(refs.TruckId);
        var fuelText = document.TryGetOptionalUnitScalar(truckUnit, "fuel_relative");
        if (fuelText is not null &&
            decimal.TryParse(
                fuelText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsedFuel))
        {
            fuel = parsedFuel;
        }

        var trailerUnits = refs.TrailerId is null
            ? 0
            : ResolveTrailerChain(document, refs.TrailerId).Count;

        return new ScsActiveVehicleState(
            refs.TruckId,
            refs.TrailerId,
            trailerUnits,
            fuel);
    }

    public async Task<ScsSaveEditResult> RepairActiveTruckAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default)
    {
        ValidateSaveReference(save);

        var source = await _codec.ReadAsync(save.GameSiiPath, cancellationToken);
        var document = ScsSiiUnitDocument.Parse(source.Content);
        var refs = ResolveActiveReferences(document);
        var truckUnit = document.GetRequiredUniqueUnit(refs.TruckId);

        var updated = document.SetUnitScalarFamily(
            truckUnit,
            TruckWearKeys,
            TruckWheelWearPrefixes,
            "0",
            out var replacements);

        return await ApplyAsync(
            save,
            $"Aktiven Truck reparieren ({replacements} Werte)",
            updated,
            cancellationToken);
    }

    public async Task<ScsSaveEditResult> SetActiveTruckFuelAsync(
        ScsSaveReference save,
        decimal fuelRelative,
        CancellationToken cancellationToken = default)
    {
        ValidateSaveReference(save);

        if (fuelRelative < 0m || fuelRelative > 1m)
            throw new ArgumentOutOfRangeException(
                nameof(fuelRelative),
                "Der Kraftstoffwert muss zwischen 0 und 1 liegen.");

        var source = await _codec.ReadAsync(save.GameSiiPath, cancellationToken);
        var document = ScsSiiUnitDocument.Parse(source.Content);
        var refs = ResolveActiveReferences(document);
        var truckUnit = document.GetRequiredUniqueUnit(refs.TruckId);

        var value = fuelRelative.ToString("0.############################", CultureInfo.InvariantCulture);
        var updated = document.SetRequiredUnitScalar(truckUnit, "fuel_relative", value);

        return await ApplyAsync(
            save,
            $"Kraftstoff des aktiven Trucks ändern: {value}",
            updated,
            cancellationToken);
    }

    public async Task<ScsSaveEditResult> RepairActiveTrailerAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default)
    {
        ValidateSaveReference(save);

        var source = await _codec.ReadAsync(save.GameSiiPath, cancellationToken);
        var document = ScsSiiUnitDocument.Parse(source.Content);
        var refs = ResolveActiveReferences(document);

        if (refs.TrailerId is null)
            throw new ScsSaveEditException("Am aktiven Truck ist kein Trailer im Save referenziert.");

        var chain = ResolveTrailerChain(document, refs.TrailerId);
        var updated = document;
        var totalReplacements = 0;

        foreach (var trailerId in chain)
        {
            var unit = updated.GetRequiredUniqueUnit(trailerId);
            updated = updated.SetUnitScalarFamily(
                unit,
                TrailerWearKeys,
                TrailerWheelWearPrefixes,
                "0",
                out var replacements);
            totalReplacements += replacements;
        }

        return await ApplyAsync(
            save,
            $"Aktiven Trailer reparieren ({chain.Count} Units, {totalReplacements} Werte)",
            updated,
            cancellationToken);
    }

    private async Task<ScsSaveEditResult> ApplyAsync(
        ScsSaveReference save,
        string operation,
        ScsSiiUnitDocument updated,
        CancellationToken cancellationToken) =>
        await _saveEditService.ApplyAsync(
            new ScsSaveEditRequest(
                save.GameSiiPath,
                operation,
                updated.ToText()),
            cancellationToken);

    private static ActiveReferences ResolveActiveReferences(ScsSiiUnitDocument document)
    {
        var assignedVehiclesId = NormalizeReference(
            document.GetRequiredUniqueScalar("assigned_vehicles"),
            "assigned_vehicles");

        var playerVehicles = document.GetRequiredUniqueUnit(assignedVehiclesId);
        var truckId = NormalizeReference(
            document.GetRequiredUnitScalar(playerVehicles, "vehicle"),
            "vehicle");

        var trailerValue = document.TryGetOptionalUnitScalar(playerVehicles, "trailer");
        var trailerId = string.IsNullOrWhiteSpace(trailerValue) ||
                        string.Equals(trailerValue.Trim(), "null", StringComparison.OrdinalIgnoreCase)
            ? null
            : NormalizeReference(trailerValue, "trailer");

        return new ActiveReferences(truckId, trailerId);
    }

    private static IReadOnlyList<string> ResolveTrailerChain(
        ScsSiiUnitDocument document,
        string firstTrailerId)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = firstTrailerId;

        for (var depth = 0; depth < 20; depth++)
        {
            if (!seen.Add(current))
                throw new ScsSaveEditException("Die Trailer-Kette enthält einen Zyklus.");

            result.Add(current);
            var unit = document.GetRequiredUniqueUnit(current);
            var slave = document.TryGetOptionalUnitScalar(unit, "slave_trailer");

            if (string.IsNullOrWhiteSpace(slave) ||
                string.Equals(slave.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            {
                return result;
            }

            current = NormalizeReference(slave, "slave_trailer");
        }

        throw new ScsSaveEditException(
            "Die Trailer-Kette überschreitet die Sicherheitsgrenze von 20 Units.");
    }

    private static string NormalizeReference(string value, string field)
    {
        var reference = value.Trim();
        if (reference.Length == 0 ||
            string.Equals(reference, "null", StringComparison.OrdinalIgnoreCase) ||
            reference.Any(char.IsWhiteSpace) ||
            reference.Contains('{') ||
            reference.Contains('}'))
        {
            throw new ScsSaveEditException(
                $"SII-Referenz '{field}' ist ungültig oder nicht unterstützt.");
        }

        return reference;
    }

    private static void ValidateSaveReference(ScsSaveReference save)
    {
        ArgumentNullException.ThrowIfNull(save);

        var profileDirectory = Path.GetFullPath(save.ProfileDirectory);
        var saveDirectory = Path.GetFullPath(save.SaveDirectory);
        var expectedRoot = Path.Combine(profileDirectory, "save");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expectedRoot));
        var fullSave = Path.TrimEndingDirectorySeparator(saveDirectory);

        if (!fullSave.StartsWith(
                fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ScsSaveEditException(
                "Das Save-Verzeichnis liegt außerhalb des ausgewählten Profils.");
        }

        var expectedGameSii = Path.Combine(saveDirectory, "game.sii");
        if (!string.Equals(
                Path.GetFullPath(expectedGameSii),
                Path.GetFullPath(save.GameSiiPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ScsSaveEditException("Ungültige game.sii-Auswahl.");
        }
    }

    private sealed record ActiveReferences(string TruckId, string? TrailerId);
}
