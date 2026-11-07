using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NmcScsLauncher.Infrastructure;

/// <summary>A deliberately small definition reader, independent of the save-file grammar.</summary>
internal static partial class ScsPowertrainDefinitionParser
{
    internal sealed record Definition(string? UnitId, string? Name, string Fingerprint, bool Ambiguous, string? Error);
    private const int MaximumExpandedCharacters = 2 * 1024 * 1024;
    private const int MaximumIncludeFiles = 128;
    private const int MaximumIncludeDepth = 16;

    internal static Definition Read(ScsDefinitionSource source, string path, string expectedType, CancellationToken cancellationToken)
    {
        try
        {
            var budget = new ExpansionBudget();
            var expanded = Expand(source, path, new HashSet<string>(StringComparer.OrdinalIgnoreCase), budget, 0, cancellationToken);
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(expanded)));
            var tokens = Tokenize(expanded);
            if (tokens.Count < 3 || !IsSyntax(tokens[0], "SiiNunit") || !IsSyntax(tokens[1], "{") || !IsSyntax(tokens[^1], "}"))
                return new(null, null, fingerprint, false, "Keine unterstützte SiiNunit-Definition.");

            var units = new List<(string Id, string? Name)>();
            var position = 2;
            while (position < tokens.Count - 1)
            {
                if (position + 3 >= tokens.Count || tokens[position].Quoted || !IsSyntax(tokens[position + 1], ":") ||
                    tokens[position + 2].Quoted || !IsSyntax(tokens[position + 3], "{"))
                    throw new InvalidDataException("Nicht unterstützter Unit-Kopf oder Vererbungsausdruck.");
                var type = tokens[position].Value;
                var id = tokens[position + 2].Value;
                if (string.IsNullOrWhiteSpace(id) || id.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '.')))
                    throw new InvalidDataException("Nicht unterstützte Unit-ID.");
                position += 4;
                var depth = 1;
                string? name = null;
                while (position < tokens.Count - 1 && depth > 0)
                {
                    var token = tokens[position];
                    if (!token.Quoted && token.Value == "{") depth++;
                    else if (!token.Quoted && token.Value == "}") depth--;
                    else if (depth == 1 && token.Value == "name" && !token.Quoted &&
                             position + 2 < tokens.Count && IsSyntax(tokens[position + 1], ":") && tokens[position + 2].Quoted)
                    {
                        if (name is not null) throw new InvalidDataException("Mehrfaches Namensattribut.");
                        name = tokens[position + 2].Value;
                    }
                    position++;
                }
                if (depth != 0) throw new InvalidDataException("Unvollständige Unit-Klammern.");
                if (type == expectedType) units.Add((id, name));
            }
            if (position != tokens.Count - 1) throw new InvalidDataException("Unvollständige SiiNunit-Klammern.");
            if (units.Count > 1) return new(null, null, fingerprint, true, "Mehrere Komponenten-Units in derselben Definition.");
            if (units.Count == 0) return new(null, null, fingerprint, false, "Die erwartete Komponenten-Unit fehlt.");
            var unit = units[0];
            var displayName = unit.Name;
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Contains("@@", StringComparison.Ordinal) ||
                displayName.Any(char.IsControl)) displayName = null;
            if (displayName?.Length > 160) displayName = displayName[..160];
            return new(unit.Id, displayName, fingerprint, false, null);
        }
        catch (Exception exception) when (ScsPowertrainDiscoveryService.IsSourceFailure(exception))
        {
            return new(null, null, string.Empty, false, $"Definition nicht vollständig auflösbar: {exception.Message}");
        }
    }

    private static string Expand(ScsDefinitionSource source, string path, HashSet<string> active, ExpansionBudget budget,
        int depth, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (depth > MaximumIncludeDepth || ++budget.Files > MaximumIncludeFiles)
            throw new InvalidDataException("Include-Limit erreicht.");
        if (!active.Add(path)) throw new InvalidDataException("Zyklischer Include.");
        try
        {
            var text = StripComments(source.ReadText(path, cancellationToken));
            budget.Characters = checked(budget.Characters + text.Length);
            if (budget.Characters > MaximumExpandedCharacters) throw new InvalidDataException("Definition überschreitet das Textlimit.");
            var expanded = IncludePattern().Replace(text, match =>
                Expand(source, ResolveInclude(path, match.Groups[1].Value), active, budget, depth + 1, cancellationToken) + "\n");
            if (expanded.Length > MaximumExpandedCharacters) throw new InvalidDataException("Expandierter Include überschreitet das Textlimit.");
            return expanded;
        }
        finally { active.Remove(path); }
    }

    private static string ResolveInclude(string owner, string include)
    {
        if (string.IsNullOrEmpty(include) || include.Contains('\\') || include.Contains(':') || include.Any(char.IsControl))
            throw new InvalidDataException("Ungültiger Include-Pfad.");
        var combined = include.StartsWith('/') ? include : owner[..(owner.LastIndexOf('/') + 1)] + include;
        var segments = new List<string>();
        foreach (var segment in combined.Split('/'))
        {
            if (segment is "" or ".") continue;
            if (segment == "..")
            {
                if (segments.Count == 0) throw new InvalidDataException("Include verlässt das Quellenverzeichnis.");
                segments.RemoveAt(segments.Count - 1);
            }
            else segments.Add(segment);
        }
        if (segments.Count == 0) throw new InvalidDataException("Include bezeichnet keine Datei.");
        return "/" + string.Join('/', segments);
    }

    private static string StripComments(string text)
    {
        var output = new StringBuilder(text.Length);
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                output.Append(character);
                if (character == '\\' && index + 1 < text.Length) output.Append(text[++index]);
                else if (character == '"') quoted = false;
                continue;
            }
            if (character == '"') { quoted = true; output.Append(character); continue; }
            if (character == '#' || (character == '/' && index + 1 < text.Length && text[index + 1] == '/'))
            {
                while (index < text.Length && text[index] != '\n') { output.Append(' '); index++; }
                if (index < text.Length) output.Append('\n');
                continue;
            }
            if (character == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                output.Append("  "); index += 2;
                while (index + 1 < text.Length && !(text[index] == '*' && text[index + 1] == '/'))
                    output.Append(text[index++] == '\n' ? '\n' : ' ');
                if (index + 1 >= text.Length) throw new InvalidDataException("Unvollständiger Blockkommentar.");
                output.Append("  "); index++;
                continue;
            }
            output.Append(character);
        }
        if (quoted) throw new InvalidDataException("Unvollständige Zeichenfolge.");
        return output.ToString();
    }

    private static List<Token> Tokenize(string text)
    {
        var result = new List<Token>();
        for (var index = 0; index < text.Length;)
        {
            if (char.IsWhiteSpace(text[index]) || text[index] == '\uFEFF') { index++; continue; }
            if (text[index] == '@') throw new InvalidDataException("Nicht unterstützte Include-Direktive.");
            if (text[index] == '"')
            {
                index++;
                var value = new StringBuilder();
                while (index < text.Length && text[index] != '"')
                {
                    if (text[index] == '\\')
                    {
                        if (++index >= text.Length) throw new InvalidDataException("Unvollständige Escape-Sequenz.");
                        var escaped = text[index++];
                        value.Append(escaped switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => escaped });
                    }
                    else value.Append(text[index++]);
                }
                if (index >= text.Length) throw new InvalidDataException("Unvollständige Zeichenfolge.");
                index++;
                result.Add(new(value.ToString(), true));
            }
            else if ("{}:[](),".Contains(text[index])) result.Add(new(text[index++].ToString(), false));
            else
            {
                var start = index++;
                while (index < text.Length && !char.IsWhiteSpace(text[index]) && !"{}:[](),\"".Contains(text[index])) index++;
                result.Add(new(text[start..index], false));
            }
            if (result.Count > 200_000) throw new InvalidDataException("Definition überschreitet das Tokenlimit.");
        }
        return result;
    }

    [GeneratedRegex("^[ \\t]*@include[ \\t]+\"([^\"\\r\\n]+)\"[ \\t]*(?:\\r?\\n|$)", RegexOptions.Multiline)]
    private static partial Regex IncludePattern();
    private sealed record Token(string Value, bool Quoted);
    private static bool IsSyntax(Token token, string value) => !token.Quoted && token.Value == value;
    private sealed class ExpansionBudget { internal int Files; internal int Characters; }
}
