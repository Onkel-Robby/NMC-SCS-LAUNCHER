using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsSaveCodec : IScsSaveCodec
{
    private readonly ScsPlainTextSaveCodec _plainTextCodec;
    private readonly IScsSaveDecoder _decoder;

    public ScsSaveCodec(
        ScsPlainTextSaveCodec plainTextCodec,
        IScsSaveDecoder decoder)
    {
        _plainTextCodec = plainTextCodec;
        _decoder = decoder;
    }

    public async Task<ScsSaveDocument> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(filePath);

        if (!File.Exists(fullPath))
            return await _plainTextCodec.ReadAsync(fullPath, cancellationToken);

        try
        {
            return await _plainTextCodec.ReadAsync(fullPath, cancellationToken);
        }
        catch (ScsSaveEditException)
        {
            var decoded = await _decoder.DecodeToTextAsync(fullPath, cancellationToken);
            _ = ScsSiiTextDocument.Parse(decoded);

            return new ScsSaveDocument(
                fullPath,
                decoded,
                ScsSaveDocumentFormat.DecodedText);
        }
    }
}
