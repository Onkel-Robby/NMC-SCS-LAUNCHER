using System.Text;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class PlainTextScsSaveCodec : IScsSaveTextCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task<string> ReadAsPlainTextAsync(
        string siiPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(siiPath))
            throw new ArgumentException("SII path is required.", nameof(siiPath));

        var fullPath = Path.GetFullPath(siiPath);
        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);

        string text;
        try
        {
            text = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException(
                "Die SII-Datei ist nicht als Klartext lesbar. Ein SCS-Save-Decoder wird benötigt.",
                ex);
        }

        var normalizedStart = text.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (!normalizedStart.StartsWith("SiiNunit", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Die SII-Datei liegt nicht im unterstützten Klartextformat vor. Verschlüsselte/Binär-Saves werden noch nicht geschrieben.");
        }

        return text;
    }
}
