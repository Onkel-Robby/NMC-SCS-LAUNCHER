using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsSaveCodec : IScsSaveCodec
{
    private readonly ScsPlainTextSaveCodec _plainTextCodec;
    private readonly IScsSaveDecoder _decoder;
    private readonly SemaphoreSlim _decodeGate = new(1, 1);
    private readonly object _cacheSync = new();
    private CachedDecodedSave? _cachedDecodedSave;

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
            var plainText = await _plainTextCodec.ReadAsync(fullPath, cancellationToken);
            InvalidateCachedPath(fullPath);
            return plainText;
        }
        catch (ScsSaveEditException)
        {
            // Binary/encrypted saves fall through to the pinned decoder.
        }

        var fingerprint = GetFingerprint(fullPath);
        if (TryGetCached(fullPath, fingerprint, out var cached))
            return cached;

        await _decodeGate.WaitAsync(cancellationToken);
        try
        {
            fingerprint = GetFingerprint(fullPath);
            if (TryGetCached(fullPath, fingerprint, out cached))
                return cached;

            var decoded = await _decoder.DecodeToTextAsync(fullPath, cancellationToken);
            _ = ScsSiiTextDocument.Parse(decoded);

            var afterDecode = GetFingerprint(fullPath);
            if (afterDecode != fingerprint)
            {
                throw new ScsSaveEditException(
                    "Die Save-Datei wurde während des Decodierens verändert. " +
                    "Der Vorgang wurde aus Sicherheitsgründen abgebrochen.");
            }

            var document = new ScsSaveDocument(
                fullPath,
                decoded,
                ScsSaveDocumentFormat.DecodedText);

            lock (_cacheSync)
            {
                _cachedDecodedSave = new CachedDecodedSave(
                    fullPath,
                    fingerprint,
                    document);
            }

            return document;
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    private bool TryGetCached(
        string fullPath,
        FileFingerprint fingerprint,
        out ScsSaveDocument document)
    {
        lock (_cacheSync)
        {
            if (_cachedDecodedSave is not null &&
                string.Equals(
                    _cachedDecodedSave.FilePath,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase) &&
                _cachedDecodedSave.Fingerprint == fingerprint)
            {
                document = _cachedDecodedSave.Document;
                return true;
            }
        }

        document = null!;
        return false;
    }

    private void InvalidateCachedPath(string fullPath)
    {
        lock (_cacheSync)
        {
            if (_cachedDecodedSave is not null &&
                string.Equals(
                    _cachedDecodedSave.FilePath,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                _cachedDecodedSave = null;
            }
        }
    }

    private static FileFingerprint GetFingerprint(string fullPath)
    {
        var info = new FileInfo(fullPath);
        info.Refresh();

        if (!info.Exists)
            throw new FileNotFoundException("Die Save-Datei wurde nicht gefunden.", fullPath);

        return new FileFingerprint(
            info.Length,
            info.LastWriteTimeUtc.Ticks);
    }

    private sealed record CachedDecodedSave(
        string FilePath,
        FileFingerprint Fingerprint,
        ScsSaveDocument Document);

    private readonly record struct FileFingerprint(
        long Length,
        long LastWriteTimeUtcTicks);
}
