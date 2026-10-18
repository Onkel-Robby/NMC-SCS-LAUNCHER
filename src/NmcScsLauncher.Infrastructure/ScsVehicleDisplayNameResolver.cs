using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

/// <summary>
/// Read-only model labels from accessories already present in game.sii.
/// Never invent a marketing name or modify save references.
/// </summary>
internal static class ScsVehicleDisplayNameResolver
{
    public static string Resolve(
        ScsSiiUnitDocument document,
        ScsSiiUnit unit,
        bool isTruck,
        int slot)
    {
        var category = isTruck ? "Truck" : "Trailer";
        var fallback = $"{category} {slot + 1} (Modell unbekannt)";
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in document.GetIndexedUnitScalars(unit, "accessories"))
        {
            var accessoryId = reference.Value.Trim();
            if (ScsSaveStructureResolver.IsNullReference(accessoryId))
                continue;

            try
            {
                var accessory = document.GetRequiredUniqueUnit(accessoryId);
                var path = document.TryGetOptionalUnitScalar(accessory, "data_path");
                var model = TryExtractModelToken(path, isTruck);
                if (model is not null)
                    candidates.Add(model);
            }
            catch (ScsSaveEditException)
            {
                // A missing or unsupported cosmetic accessory must not prevent
                // the player from inspecting or switching owned vehicles.
            }
        }

        // Mixed model roots are ambiguous (e.g. modded accessories). Fall back
        // rather than presenting a potentially incorrect vehicle identity.
        if (candidates.Count != 1)
            return fallback;

        var modelName = FormatModelToken(candidates.Single());
        return string.IsNullOrWhiteSpace(modelName)
            ? fallback
            : $"{modelName} ({category} {slot + 1})";
    }

    private static string? TryExtractModelToken(string? rawPath, bool isTruck)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        var path = rawPath.Trim();
        if (path.Length < 2 || path[0] != '"' || path[^1] != '"')
            return null;

        var parts = path[1..^1].Replace('\\', '/').Split(
            '/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 5 ||
            !parts[0].Equals("def", StringComparison.OrdinalIgnoreCase) ||
            !parts[1].Equals("vehicle", StringComparison.OrdinalIgnoreCase))
            return null;

        var kind = parts[2];
        if (isTruck
            ? !kind.Equals("truck", StringComparison.OrdinalIgnoreCase)
            : !kind.Equals("trailer_owned", StringComparison.OrdinalIgnoreCase) &&
              !kind.Equals("trailer", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = parts[3];
        return token.Length is > 0 and <= 100 &&
               token.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            ? token
            : null;
    }

    private static string FormatModelToken(string token)
    {
        var parts = token.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return string.Join(" ", parts.Select((word, index) =>
        {
            if (index == 0)
            {
                return word.ToLowerInvariant() switch
                {
                    "man" => "MAN",
                    "daf" => "DAF",
                    "scs" => "SCS",
                    "mb" => "Mercedes-Benz",
                    "vw" => "VW",
                    _ => Capitalize(word)
                };
            }

            return word.Length <= 2 && word.All(char.IsLetter)
                ? word.ToUpperInvariant()
                : Capitalize(word);
        }));
    }

    private static string Capitalize(string word) =>
        word.Length == 0
            ? word
            : string.Concat(char.ToUpperInvariant(word[0]), word[1..].ToLowerInvariant());
}
