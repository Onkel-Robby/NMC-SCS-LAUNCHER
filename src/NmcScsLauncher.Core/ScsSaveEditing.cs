using System.Text.RegularExpressions;

namespace NmcScsLauncher.Core;

public enum ScsSaveDocumentFormat
{
    PlainText = 1,
    DecodedText = 2,
    UnsupportedBinary = 3
}

public sealed record ScsSaveDocument(
    string FilePath,
    string Content,
    ScsSaveDocumentFormat Format);

public sealed record ScsSaveEditRequest(
    string FilePath,
    string Operation,
    string UpdatedContent);

public sealed record ScsSaveEditResult(
    string FilePath,
    string BackupPath,
    string Operation,
    string OriginalSha256,
    string UpdatedSha256,
    DateTimeOffset CompletedAtUtc);

public interface IScsSaveCodec
{
    Task<ScsSaveDocument> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}

public interface IScsSaveDecoder
{
    Task<string> DecodeToTextAsync(
        string filePath,
        CancellationToken cancellationToken = default);
}

public interface IScsGameProcessGuard
{
    bool IsAnyScsGameRunning();
}

public interface IScsSaveEditService
{
    Task<ScsSaveEditResult> ApplyAsync(
        ScsSaveEditRequest request,
        CancellationToken cancellationToken = default);

    Task RestoreBackupAsync(
        string backupPath,
        string targetFilePath,
        CancellationToken cancellationToken = default);
}

public sealed class ScsSaveEditException : Exception
{
    public ScsSaveEditException(string message) : base(message)
    {
    }

    public ScsSaveEditException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class ScsSiiTextDocument
{
    private static readonly Regex KeyPattern = new("^[A-Za-z0-9_]+$", RegexOptions.Compiled);
    private readonly string[] _lines;

    private ScsSiiTextDocument(string[] lines, string newline)
    {
        _lines = lines;
        Newline = newline;
    }

    public string Newline { get; }

    public static ScsSiiTextDocument Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ScsSaveEditException("Die SII-Datei ist leer.");

        var normalized = content.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (!normalized.StartsWith("SiiNunit", StringComparison.Ordinal))
            throw new ScsSaveEditException("Die Datei ist kein unterstütztes textuelles SII-Dokument.");

        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        return new ScsSiiTextDocument(lines, newline);
    }

    public string ToText() => string.Join(Newline, _lines);

    public bool TryGetScalar(string key, out string value)
    {
        ValidateKey(key);
        var matches = FindScalarMatches(key);

        if (matches.Count != 1)
        {
            value = string.Empty;
            return false;
        }

        value = matches[0].Value;
        return true;
    }

    public ScsSiiTextDocument SetScalar(string key, string value)
    {
        ValidateKey(key);
        ValidateValue(value);

        var matches = FindScalarMatches(key);
        if (matches.Count == 0)
            throw new ScsSaveEditException($"SII-Wert '{key}' wurde nicht gefunden.");
        if (matches.Count > 1)
            throw new ScsSaveEditException(
                $"SII-Wert '{key}' ist mehrfach vorhanden und wird deshalb nicht automatisch verändert.");

        var match = matches[0];
        var clone = (string[])_lines.Clone();
        clone[match.Index] = $"{match.Indent}{key}: {value}";
        return new ScsSiiTextDocument(clone, Newline);
    }

    private List<ScalarMatch> FindScalarMatches(string key)
    {
        var result = new List<ScalarMatch>();

        for (var index = 0; index < _lines.Length; index++)
        {
            if (TryParseScalarLine(_lines[index], key, out var indent, out var value))
                result.Add(new ScalarMatch(index, indent, value));
        }

        return result;
    }

    private static bool TryParseScalarLine(
        string line,
        string key,
        out string indent,
        out string value)
    {
        indent = string.Empty;
        value = string.Empty;

        var position = 0;
        while (position < line.Length && (line[position] == ' ' || line[position] == '\t'))
            position++;

        var keyStart = position;
        if (line.Length - position < key.Length ||
            !line.AsSpan(position, key.Length).SequenceEqual(key.AsSpan()))
        {
            return false;
        }

        position += key.Length;
        while (position < line.Length && (line[position] == ' ' || line[position] == '\t'))
            position++;

        if (position >= line.Length || line[position] != ':')
            return false;

        position++;
        while (position < line.Length && (line[position] == ' ' || line[position] == '\t'))
            position++;

        indent = line[..keyStart];
        value = line[position..].TrimEnd();
        return true;
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || !KeyPattern.IsMatch(key))
            throw new ArgumentException("SII-Schlüssel enthält ungültige Zeichen.", nameof(key));
    }

    private static void ValidateValue(string value)
    {
        if (value.Contains('\r') || value.Contains('\n'))
            throw new ArgumentException("SII-Werte dürfen keine Zeilenumbrüche enthalten.", nameof(value));
    }

    private sealed record ScalarMatch(int Index, string Indent, string Value);
}
