using System.Text.RegularExpressions;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

internal sealed class ScsSiiUnitDocument
{
    private static readonly Regex UnitHeaderPattern = new(
        @"^\s*(?<type>[A-Za-z0-9_]+)\s*:\s*(?<id>[^\s{]+)\s*\{\s*$",
        RegexOptions.Compiled);

    private readonly string[] _lines;

    private ScsSiiUnitDocument(string[] lines, string newline)
    {
        _lines = lines;
        Newline = newline;
    }

    public string Newline { get; }

    public static ScsSiiUnitDocument Parse(string content)
    {
        _ = ScsSiiTextDocument.Parse(content);
        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return new ScsSiiUnitDocument(lines, newline);
    }

    public string ToText() => string.Join(Newline, _lines);

    public string GetRequiredUniqueScalar(string key)
    {
        var matches = new List<string>();

        for (var i = 0; i < _lines.Length; i++)
        {
            if (TryParseScalar(_lines[i], out var parsedKey, out var value) &&
                string.Equals(parsedKey, key, StringComparison.Ordinal))
            {
                matches.Add(value);
            }
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ScsSaveEditException($"SII-Feld '{key}' wurde nicht gefunden."),
            _ => throw new ScsSaveEditException(
                $"SII-Feld '{key}' ist mehrfach vorhanden und kann nicht eindeutig aufgelöst werden.")
        };
    }

    public ScsSiiUnit GetRequiredUniqueUnit(string id)
    {
        var matches = FindUnits()
            .Where(unit => string.Equals(unit.Id, id, StringComparison.Ordinal))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new ScsSaveEditException($"SII-Unit '{id}' wurde nicht gefunden."),
            _ => throw new ScsSaveEditException(
                $"SII-Unit '{id}' ist mehrfach vorhanden und kann nicht eindeutig aufgelöst werden.")
        };
    }

    public string GetRequiredUnitScalar(ScsSiiUnit unit, string key)
    {
        var matches = FindUnitScalarLines(unit, key, includeIndexed: false);

        return matches.Count switch
        {
            1 => matches[0].Value,
            0 => throw new ScsSaveEditException(
                $"SII-Feld '{key}' wurde in Unit '{unit.Id}' nicht gefunden."),
            _ => throw new ScsSaveEditException(
                $"SII-Feld '{key}' ist in Unit '{unit.Id}' mehrfach vorhanden.")
        };
    }

    public string? TryGetOptionalUnitScalar(ScsSiiUnit unit, string key)
    {
        var matches = FindUnitScalarLines(unit, key, includeIndexed: false);

        return matches.Count switch
        {
            0 => null,
            1 => matches[0].Value,
            _ => throw new ScsSaveEditException(
                $"SII-Feld '{key}' ist in Unit '{unit.Id}' mehrfach vorhanden.")
        };
    }

    public ScsSiiUnitDocument SetRequiredUnitScalar(ScsSiiUnit unit, string key, string value)
    {
        ValidateScalarValue(value);
        var matches = FindUnitScalarLines(unit, key, includeIndexed: false);

        if (matches.Count == 0)
            throw new ScsSaveEditException(
                $"SII-Feld '{key}' wurde in Unit '{unit.Id}' nicht gefunden.");
        if (matches.Count > 1)
            throw new ScsSaveEditException(
                $"SII-Feld '{key}' ist in Unit '{unit.Id}' mehrfach vorhanden.");

        var clone = (string[])_lines.Clone();
        var match = matches[0];
        clone[match.Index] = $"{match.Indent}{match.Key}: {value}";
        return new ScsSiiUnitDocument(clone, Newline);
    }

    public ScsSiiUnitDocument SetUnitScalarFamily(
        ScsSiiUnit unit,
        IReadOnlySet<string> exactKeys,
        IReadOnlySet<string> indexedPrefixes,
        string value,
        out int replacements)
    {
        ValidateScalarValue(value);
        var clone = (string[])_lines.Clone();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        replacements = 0;

        for (var i = unit.StartLine + 1; i < unit.EndLine; i++)
        {
            if (!TryParseScalar(clone[i], out var key, out _))
                continue;

            var match =
                exactKeys.Contains(key) ||
                indexedPrefixes.Any(prefix => IsIndexedKey(key, prefix));

            if (!match)
                continue;

            if (!seenKeys.Add(key))
            {
                throw new ScsSaveEditException(
                    $"SII-Feld '{key}' ist in Unit '{unit.Id}' mehrfach vorhanden.");
            }

            var indentLength = 0;
            while (indentLength < clone[i].Length &&
                   (clone[i][indentLength] == ' ' || clone[i][indentLength] == '\t'))
            {
                indentLength++;
            }

            clone[i] = $"{clone[i][..indentLength]}{key}: {value}";
            replacements++;
        }

        if (replacements == 0)
            throw new ScsSaveEditException(
                $"In Unit '{unit.Id}' wurden keine unterstützten Fahrzeugwerte gefunden.");

        return new ScsSiiUnitDocument(clone, Newline);
    }

    private IReadOnlyList<ScsSiiUnit> FindUnits()
    {
        var result = new List<ScsSiiUnit>();

        for (var i = 0; i < _lines.Length; i++)
        {
            var match = UnitHeaderPattern.Match(_lines[i]);
            if (!match.Success)
                continue;

            var end = -1;
            for (var j = i + 1; j < _lines.Length; j++)
            {
                if (string.Equals(_lines[j].Trim(), "}", StringComparison.Ordinal))
                {
                    end = j;
                    break;
                }
            }

            if (end < 0)
                throw new ScsSaveEditException(
                    $"SII-Unit '{match.Groups["id"].Value}' ist nicht korrekt abgeschlossen.");

            result.Add(new ScsSiiUnit(
                match.Groups["type"].Value,
                match.Groups["id"].Value,
                i,
                end));

            i = end;
        }

        return result;
    }

    private List<ScalarLine> FindUnitScalarLines(
        ScsSiiUnit unit,
        string key,
        bool includeIndexed)
    {
        var result = new List<ScalarLine>();

        for (var i = unit.StartLine + 1; i < unit.EndLine; i++)
        {
            if (!TryParseScalar(_lines[i], out var parsedKey, out var value))
                continue;

            var isMatch = string.Equals(parsedKey, key, StringComparison.Ordinal) ||
                (includeIndexed &&
                 parsedKey.StartsWith(key + "[", StringComparison.Ordinal) &&
                 parsedKey.EndsWith("]", StringComparison.Ordinal));

            if (!isMatch)
                continue;

            var indentLength = 0;
            while (indentLength < _lines[i].Length &&
                   (_lines[i][indentLength] == ' ' || _lines[i][indentLength] == '\t'))
            {
                indentLength++;
            }

            result.Add(new ScalarLine(
                i,
                _lines[i][..indentLength],
                parsedKey,
                value));
        }

        return result;
    }

    private static bool IsIndexedKey(string key, string prefix)
    {
        var expectedPrefix = prefix + "[";
        if (!key.StartsWith(expectedPrefix, StringComparison.Ordinal) ||
            !key.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }

        var index = key[expectedPrefix.Length..^1];
        return index.Length > 0 && index.All(char.IsDigit);
    }

    private static bool TryParseScalar(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        var trimmed = line.TrimStart(' ', '\t');
        if (trimmed.Length == 0 || trimmed == "}" || trimmed.EndsWith("{", StringComparison.Ordinal))
            return false;

        var colon = trimmed.IndexOf(':');
        if (colon <= 0)
            return false;

        key = trimmed[..colon].TrimEnd();
        if (key.Length == 0 || key.Any(char.IsWhiteSpace))
            return false;

        value = trimmed[(colon + 1)..].Trim();
        return true;
    }

    private static void ValidateScalarValue(string value)
    {
        if (value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("SII-Werte dürfen keine Zeilenumbrüche enthalten.", nameof(value));
    }

    private sealed record ScalarLine(int Index, string Indent, string Key, string Value);
}

internal sealed record ScsSiiUnit(
    string Type,
    string Id,
    int StartLine,
    int EndLine);
