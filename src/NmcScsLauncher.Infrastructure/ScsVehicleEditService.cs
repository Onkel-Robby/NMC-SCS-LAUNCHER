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
        EnsureUnitType(truckUnit, "vehicle", "aktiver Truck");

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

    public async Task<ScsVehicleInventory> GetVehicleInventoryAsync(
        ScsSaveReference save,
        CancellationToken cancellationToken = default)
    {
        ValidateSaveReference(save);

        var source = await _codec.ReadAsync(save.GameSiiPath, cancellationToken);
        var document = ScsSiiUnitDocument.Parse(source.Content);
        var refs = ResolveActiveReferences(document);
        var player = ResolvePlayerUnit(document);

        var trucks = ReadInventory(
            document,
            player,
            "trucks",
            "vehicle",
            refs.TruckId);

        var trailers = ReadInventory(
            document,
            player,
            "trailers",
            "trailer",
            refs.TrailerId);

        return new ScsVehicleInventory(
            refs.TruckId,
            refs.TrailerId,
            trucks,
            trailers);
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
        EnsureUnitType(truckUnit, "vehicle", "aktiver Truck");

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
        EnsureUnitType(truckUnit, "vehicle", "aktiver Truck");

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
            EnsureUnitType(unit, "trailer", "aktiver Trailer");
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

    public async Task<ScsSaveEditResult> SwitchActiveTrailerAsync(
        ScsSaveReference save,
        string targetTrailerId,
        CancellationToken cancellationToken = default)
    {
        ValidateSaveReference(save);

        var targetId = NormalizeReference(targetTrailerId, nameof(targetTrailerId));
        var source = await _codec.ReadAsync(save.GameSiiPath, cancellationToken);
        var document = ScsSiiUnitDocument.Parse(source.Content);
        var refs = ResolveActiveReferences(document);

        if (refs.TrailerId is null)
        {
            throw new ScsSaveEditException(
                "Ein Trailer-Wechsel wird nur unterstützt, wenn bereits ein aktiver Trailer referenziert ist.");
        }

        if (string.Equals(refs.TrailerId, targetId, StringComparison.Ordinal))
            throw new ScsSaveEditException("Der ausgewählte Trailer ist bereits aktiv.");

        var inventory = BuildInventory(document, refs);
        if (!inventory.Trailers.Any(item => string.Equals(item.Id, targetId, StringComparison.Ordinal)))
        {
            throw new ScsSaveEditException(
                "Der ausgewählte Trailer ist im Besitz-Array des Profils nicht eindeutig vorhanden.");
        }

        _ = ResolveTrailerChain(document, targetId);

        var matchingPlayerVehicleUnits = GetPlayerVehicleUnitsForTruck(
            document,
            refs.TruckId);

        if (matchingPlayerVehicleUnits.Length == 0)
        {
            throw new ScsSaveEditException(
                "Es wurde keine player_vehicles-Unit für den aktiven Truck gefunden.");
        }

        var updated = document;
        var replacements = 0;

        foreach (var unit in matchingPlayerVehicleUnits)
        {
            var trailerValue = updated.TryGetOptionalUnitScalar(unit, "trailer");
            if (trailerValue is null)
            {
                throw new ScsSaveEditException(
                    $"player_vehicles-Unit '{unit.Id}' enthält kein Trailer-Feld.");
            }

            if (!string.Equals(trailerValue.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            {
                var currentId = NormalizeReference(trailerValue, "trailer");
                if (!string.Equals(currentId, refs.TrailerId, StringComparison.Ordinal))
                {
                    throw new ScsSaveEditException(
                        "Mehrere unterschiedliche Trailer-Zuordnungen für den aktiven Truck wurden erkannt. " +
                        "Der Wechsel wird aus Sicherheitsgründen abgebrochen.");
                }
            }

            var currentUnit = updated.GetRequiredUniqueUnit(unit.Id);
            updated = updated.SetRequiredUnitScalar(currentUnit, "trailer", targetId);
            replacements++;
        }

        var resultRefs = ResolveActiveReferences(updated);
        if (!string.Equals(resultRefs.TruckId, refs.TruckId, StringComparison.Ordinal) ||
            !string.Equals(resultRefs.TrailerId, targetId, StringComparison.Ordinal))
        {
            throw new ScsSaveEditException(
                "Die Trailer-Zuordnung konnte nach der Änderung nicht konsistent validiert werden.");
        }

        return await ApplyAsync(
            save,
            $"Aktiven Trailer wechseln: {refs.TrailerId} -> {targetId} ({replacements} Referenzen)",
            updated,
            cancellationToken);
    }

    public async Task<ScsSaveEditResult> SwitchActiveTruckAsync(
        ScsSaveReference save,
        string targetTruckId,
        CancellationToken cancellationToken = default)
    {
        ValidateSaveReference(save);

        var targetId = NormalizeReference(targetTruckId, nameof(targetTruckId));
        var source = await _codec.ReadAsync(save.GameSiiPath, cancellationToken);
        var document = ScsSiiUnitDocument.Parse(source.Content);
        var refs = ResolveActiveReferences(document);

        if (string.Equals(refs.TruckId, targetId, StringComparison.Ordinal))
            throw new ScsSaveEditException("Der ausgewählte Truck ist bereits aktiv.");

        var inventory = BuildInventory(document, refs);
        if (!inventory.Trucks.Any(item => string.Equals(item.Id, targetId, StringComparison.Ordinal)))
        {
            throw new ScsSaveEditException(
                "Der ausgewählte Truck ist im Besitz-Array des Profils nicht eindeutig vorhanden.");
        }

        var targetTruckUnit = document.GetRequiredUniqueUnit(targetId);
        EnsureUnitType(targetTruckUnit, "vehicle", "Ziel-Truck");

        var currentGarage = ResolveGarageSlot(document, refs.TruckId);
        var targetGarage = ResolveGarageSlot(document, targetId);
        var targetCity = ResolveGarageCity(targetGarage.GarageId);

        var player = ResolvePlayerUnit(document);
        _ = document.GetRequiredUnitScalar(player, "hq_city");

        var matchingPlayerVehicleUnits = GetPlayerVehicleUnitsForTruck(document, refs.TruckId);
        if (matchingPlayerVehicleUnits.Count == 0)
        {
            throw new ScsSaveEditException(
                "Es wurde keine player_vehicles-Unit für den aktiven Truck gefunden.");
        }

        var updated = document;
        foreach (var unit in matchingPlayerVehicleUnits)
        {
            var currentUnit = updated.GetRequiredUniqueUnit(unit.Id);
            updated = updated.SetRequiredUnitScalar(currentUnit, "vehicle", targetId);
        }

        var currentGarageUnit = updated.GetRequiredUniqueUnit(currentGarage.GarageId);
        updated = updated.SetRequiredIndexedUnitScalar(
            currentGarageUnit,
            "drivers",
            currentGarage.Slot,
            targetGarage.DriverValue);

        var targetGarageUnit = updated.GetRequiredUniqueUnit(targetGarage.GarageId);
        updated = updated.SetRequiredIndexedUnitScalar(
            targetGarageUnit,
            "drivers",
            targetGarage.Slot,
            currentGarage.DriverValue);

        var updatedPlayer = updated.GetRequiredUniqueUnit(player.Id);
        updated = updated.SetRequiredUnitScalar(updatedPlayer, "hq_city", targetCity);

        var resultRefs = ResolveActiveReferences(updated);
        if (!string.Equals(resultRefs.TruckId, targetId, StringComparison.Ordinal))
        {
            throw new ScsSaveEditException(
                "Die Truck-Zuordnung konnte nach der Änderung nicht konsistent validiert werden.");
        }

        var validatedCurrentGarage = ResolveGarageSlot(updated, refs.TruckId);
        var validatedTargetGarage = ResolveGarageSlot(updated, targetId);

        if (!string.Equals(
                validatedCurrentGarage.DriverValue,
                targetGarage.DriverValue,
                StringComparison.Ordinal) ||
            !string.Equals(
                validatedTargetGarage.DriverValue,
                currentGarage.DriverValue,
                StringComparison.Ordinal))
        {
            throw new ScsSaveEditException(
                "Die Fahrer-Zuordnung der beteiligten Garagen konnte nicht konsistent validiert werden.");
        }

        var validatedPlayer = ResolvePlayerUnit(updated);
        var validatedHq = updated.GetRequiredUnitScalar(validatedPlayer, "hq_city").Trim();
        if (!string.Equals(validatedHq, targetCity, StringComparison.Ordinal))
        {
            throw new ScsSaveEditException(
                "Die neue HQ-Stadt konnte nach dem Truck-Wechsel nicht konsistent validiert werden.");
        }

        return await ApplyAsync(
            save,
            $"Aktiven Truck wechseln: {refs.TruckId} -> {targetId}",
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

    private static IReadOnlyList<ScsSiiUnit> GetPlayerVehicleUnitsForTruck(
        ScsSiiUnitDocument document,
        string truckId)
    {
        return document
            .GetUnitsByType("player_vehicles")
            .Where(unit =>
            {
                var vehicle = document.TryGetOptionalUnitScalar(unit, "vehicle");
                if (vehicle is null ||
                    string.Equals(vehicle.Trim(), "null", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return string.Equals(
                    NormalizeReference(vehicle, "vehicle"),
                    truckId,
                    StringComparison.Ordinal);
            })
            .ToArray();
    }

    private static GarageSlot ResolveGarageSlot(
        ScsSiiUnitDocument document,
        string truckId)
    {
        var matches = new List<GarageSlot>();

        foreach (var garage in document.GetUnitsByType("garage"))
        {
            var vehicles = document.GetIndexedUnitScalars(garage, "vehicles");
            if (vehicles.Count == 0)
                continue;

            var drivers = document
                .GetIndexedUnitScalars(garage, "drivers")
                .ToDictionary(item => item.Index);

            foreach (var vehicle in vehicles)
            {
                if (string.Equals(
                        vehicle.Value.Trim(),
                        "null",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var vehicleId = NormalizeReference(vehicle.Value, "garage vehicles");
                if (!string.Equals(vehicleId, truckId, StringComparison.Ordinal))
                    continue;

                if (!drivers.TryGetValue(vehicle.Index, out var driver))
                {
                    throw new ScsSaveEditException(
                        $"Garage '{garage.Id}' enthält für Fahrzeug-Slot {vehicle.Index} keinen passenden Fahrer-Slot.");
                }

                var driverValue = NormalizeNullableReference(
                    driver.Value,
                    $"drivers[{vehicle.Index}]");

                matches.Add(new GarageSlot(
                    garage.Id,
                    vehicle.Index,
                    driverValue));
            }
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ScsSaveEditException(
                $"Truck '{truckId}' wurde in keiner eindeutigen Garage gefunden."),
            _ => throw new ScsSaveEditException(
                $"Truck '{truckId}' ist in mehreren Garagen/Fahrzeug-Slots referenziert.")
        };
    }

    private static string ResolveGarageCity(string garageId)
    {
        const string prefix = "garage.";
        if (!garageId.StartsWith(prefix, StringComparison.Ordinal) ||
            garageId.Length <= prefix.Length)
        {
            throw new ScsSaveEditException(
                $"Garage-ID '{garageId}' kann keiner HQ-Stadt sicher zugeordnet werden.");
        }

        return NormalizeReference(garageId[prefix.Length..], "garage city");
    }

    private static string NormalizeNullableReference(string value, string field)
    {
        var reference = value.Trim();
        if (string.Equals(reference, "null", StringComparison.OrdinalIgnoreCase))
            return "null";

        return NormalizeReference(reference, field);
    }

    private static ScsVehicleInventory BuildInventory(
        ScsSiiUnitDocument document,
        ActiveReferences refs)
    {
        var player = ResolvePlayerUnit(document);

        return new ScsVehicleInventory(
            refs.TruckId,
            refs.TrailerId,
            ReadInventory(document, player, "trucks", "vehicle", refs.TruckId),
            ReadInventory(document, player, "trailers", "trailer", refs.TrailerId));
    }

    private static IReadOnlyList<ScsVehicleInventoryItem> ReadInventory(
        ScsSiiUnitDocument document,
        ScsSiiUnit player,
        string arrayPrefix,
        string expectedUnitType,
        string? activeId)
    {
        var indexed = document.GetIndexedUnitScalars(player, arrayPrefix);
        var result = new List<ScsVehicleInventoryItem>();
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in indexed)
        {
            var id = NormalizeReference(item.Value, arrayPrefix);
            if (!ids.Add(id))
            {
                throw new ScsSaveEditException(
                    $"SII-Array '{arrayPrefix}' enthält die Referenz '{id}' mehrfach.");
            }

            var unit = document.GetRequiredUniqueUnit(id);
            EnsureUnitType(unit, expectedUnitType, arrayPrefix);

            result.Add(new ScsVehicleInventoryItem(
                id,
                item.Index,
                activeId is not null &&
                string.Equals(id, activeId, StringComparison.Ordinal)));
        }

        return result;
    }

    private static ScsSiiUnit ResolvePlayerUnit(ScsSiiUnitDocument document)
    {
        var candidates = document
            .GetUnitsByType("player")
            .Where(unit => document.TryGetOptionalUnitScalar(unit, "assigned_vehicles") is not null)
            .ToArray();

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new ScsSaveEditException(
                "Keine eindeutige player-Unit mit assigned_vehicles wurde gefunden."),
            _ => throw new ScsSaveEditException(
                "Mehrere player-Units mit assigned_vehicles wurden gefunden.")
        };
    }

    private static ActiveReferences ResolveActiveReferences(ScsSiiUnitDocument document)
    {
        var player = ResolvePlayerUnit(document);
        var assignedVehiclesId = NormalizeReference(
            document.GetRequiredUnitScalar(player, "assigned_vehicles"),
            "assigned_vehicles");

        var playerVehicles = document.GetRequiredUniqueUnit(assignedVehiclesId);
        EnsureUnitType(playerVehicles, "player_vehicles", "assigned_vehicles");

        var truckId = NormalizeReference(
            document.GetRequiredUnitScalar(playerVehicles, "vehicle"),
            "vehicle");

        var truckUnit = document.GetRequiredUniqueUnit(truckId);
        EnsureUnitType(truckUnit, "vehicle", "vehicle");

        var trailerValue = document.TryGetOptionalUnitScalar(playerVehicles, "trailer");
        var trailerId = string.IsNullOrWhiteSpace(trailerValue) ||
                        string.Equals(trailerValue.Trim(), "null", StringComparison.OrdinalIgnoreCase)
            ? null
            : NormalizeReference(trailerValue, "trailer");

        if (trailerId is not null)
        {
            var trailerUnit = document.GetRequiredUniqueUnit(trailerId);
            EnsureUnitType(trailerUnit, "trailer", "trailer");
        }

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
            EnsureUnitType(unit, "trailer", "Trailer-Kette");

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

    private static void EnsureUnitType(
        ScsSiiUnit unit,
        string expectedType,
        string context)
    {
        if (!string.Equals(unit.Type, expectedType, StringComparison.Ordinal))
        {
            throw new ScsSaveEditException(
                $"SII-Referenz '{context}' zeigt auf Unit-Typ '{unit.Type}' statt '{expectedType}'.");
        }
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

    private sealed record GarageSlot(
        string GarageId,
        int Slot,
        string DriverValue);

    private sealed record ActiveReferences(string TruckId, string? TrailerId);
}
