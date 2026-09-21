using System.Text;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsPlainTextSaveCodec : IScsSaveCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<ScsSaveDocument> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            throw new ScsSaveEditException($"SII-Datei wurde nicht gefunden: {fullPath}");

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        if (bytes.Length == 0)
            throw new ScsSaveEditException("Die SII-Datei ist leer.");

        if (bytes.Contains((byte)0))
            throw new ScsSaveEditException(
                "Die SII-Datei liegt in einem binären/verschlüsselten Format vor. " +
                "Dieser Stand schreibt solche Dateien absichtlich noch nicht.");

        string content;
        try
        {
            content = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ScsSaveEditException(
                "Die SII-Datei ist nicht als unterstützter UTF-8-Text lesbar.", ex);
        }

        _ = ScsSiiTextDocument.Parse(content);

        return new ScsSaveDocument(
            fullPath,
            content,
            ScsSaveDocumentFormat.PlainText);
    }
}
