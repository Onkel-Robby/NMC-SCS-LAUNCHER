using System.Text.RegularExpressions;

namespace NmcScsLauncher.Core;

public enum ScsSaveDocumentFormat
{
    PlainText = 1,
    UnsupportedBinary = 2
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

        var normalized = content.TrimStart('﻿', ' ', '\t', '\r', '\n');
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
        var prefix = $" {key}:";

        foreach (var line in _lines)
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            value = line[prefix.Length..].Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    public ScsSiiTextDocument SetScalar(string key, string value)
    {
        ValidateKey(key);
        ValidateValue(value);

        var prefix = $" {key}:";
        var clone = (string[])_lines.Clone();

        for (var index = 0; index < clone.Length; index++)
        {
            if (!clone[index].StartsWith(prefix, StringComparison.Ordinal))
                continue;

            clone[index] = $"{prefix} {value}";
            return new ScsSiiTextDocument(clone, Newline);
        }

        throw new ScsSaveEditException($"SII-Wert '{key}' wurde nicht gefunden.");
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
}
