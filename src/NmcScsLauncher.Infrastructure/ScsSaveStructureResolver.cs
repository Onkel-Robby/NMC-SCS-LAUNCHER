using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

internal static class ScsSaveStructureResolver
{
    public static ScsSiiUnit ResolveEconomyUnit(ScsSiiUnitDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var units = document.GetUnitsByType("economy");

        return units.Count switch
        {
            1 => units[0],
            0 => throw new ScsSaveEditException(
                "Keine economy-Unit im Save gefunden."),
            _ => throw new ScsSaveEditException(
                "Mehrere economy-Units im Save gefunden; der Save kann nicht eindeutig aufgelöst werden.")
        };
    }

    public static ScsSiiUnit ResolvePlayerUnit(ScsSiiUnitDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var economies = document.GetUnitsByType("economy");
        if (economies.Count > 1)
        {
            throw new ScsSaveEditException(
                "Mehrere economy-Units im Save gefunden; die Player-Referenz ist nicht eindeutig.");
        }

        if (economies.Count == 1)
        {
            var playerReference = document.TryGetOptionalUnitScalar(economies[0], "player");
            if (!IsNullReference(playerReference))
            {
                var playerId = NormalizeReference(playerReference!, "economy.player");
                var player = document.GetRequiredUniqueUnit(playerId);
                EnsureUnitType(player, "player", "economy.player");
                return player;
            }
        }

        var players = document.GetUnitsByType("player");
        return players.Count switch
        {
            1 => players[0],
            0 => throw new ScsSaveEditException(
                "Keine player-Unit im Save gefunden."),
            _ => throw new ScsSaveEditException(
                "Mehrere player-Units wurden gefunden, aber economy.player liefert keine eindeutige Referenz.")
        };
    }

    public static ScsSiiUnit ResolveBankUnit(ScsSiiUnitDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var economies = document.GetUnitsByType("economy");
        if (economies.Count > 1)
        {
            throw new ScsSaveEditException(
                "Mehrere economy-Units im Save gefunden; die Bank-Referenz ist nicht eindeutig.");
        }

        if (economies.Count == 1)
        {
            var bankReference = document.TryGetOptionalUnitScalar(economies[0], "bank");
            if (!IsNullReference(bankReference))
            {
                var bankId = NormalizeReference(bankReference!, "economy.bank");
                var bank = document.GetRequiredUniqueUnit(bankId);
                EnsureUnitType(bank, "bank", "economy.bank");
                return bank;
            }
        }

        var banks = document.GetUnitsByType("bank");
        return banks.Count switch
        {
            1 => banks[0],
            0 => throw new ScsSaveEditException(
                "Keine bank-Unit im Save gefunden."),
            _ => throw new ScsSaveEditException(
                "Mehrere bank-Units wurden gefunden, aber economy.bank liefert keine eindeutige Referenz.")
        };
    }

    public static bool IsNullReference(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value.Trim(), "null", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeReference(string value, string field)
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

    public static void EnsureUnitType(
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
}
