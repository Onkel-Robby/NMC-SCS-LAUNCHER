using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace NmcScsLauncher.Infrastructure;

/// <summary>A bounded ZIP reader whose central directory and local records describe the same files.</summary>
internal sealed class ScsZipDefinitionSource : ScsDefinitionSource
{
    private static readonly Encoding LegacyEncoding = CodePagesEncodingProvider.Instance.GetEncoding(437)!;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, (ZipArchiveEntry Entry, uint Crc)> _files = new(StringComparer.Ordinal);

    internal ScsZipDefinitionSource(FileStream stream)
    {
        var index = ReadIndex(stream);
        ValidateLocalRecords(stream, index);
        stream.Position = 0;
        // APPNOTE Appendix D: bit 11 selects UTF-8; otherwise the original ZIP encoding is CP437.
        _archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false, LegacyEncoding);
        try
        {
            if (_archive.Entries.Count != index.Entries.Count) throw InvalidIndex();
            for (var i = 0; i < index.Entries.Count; i++)
            {
                var record = index.Entries[i];
                var entry = _archive.Entries[i];
                if (entry.FullName != record.Name || entry.Length != record.Length || entry.CompressedLength != record.CompressedLength)
                    throw InvalidIndex();
                if (!record.Directory) _files.Add(record.Path, (entry, record.Crc));
            }
        }
        catch
        {
            _archive.Dispose();
            throw;
        }
    }

    public override IReadOnlyList<string> GetFiles(string virtualDirectory, CancellationToken cancellationToken = default)
    {
        CheckAvailable(cancellationToken);
        var prefix = ValidatePath(virtualDirectory, directory: true).TrimEnd('/') + "/";
        var files = new List<string>();
        foreach (var path in _files.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (path.StartsWith(prefix, StringComparison.Ordinal)) files.Add(path);
        }
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    public override string ReadText(string virtualPath, CancellationToken cancellationToken = default)
    {
        CheckAvailable(cancellationToken);
        virtualPath = ValidatePath(virtualPath);
        if (!_files.TryGetValue(virtualPath, out var file))
            throw new FileNotFoundException("Definition nicht im ZIP-Archiv gefunden.", virtualPath);
        if (file.Entry.Length > MaximumTextBytes || file.Entry.CompressedLength > MaximumTextBytes * 2L)
            throw new InvalidDataException("Die Definitionsdatei ist zu groß.");
        using var input = file.Entry.Open();
        var bytes = ReadContent(input, file.Entry.Length, cancellationToken);
        if (Crc32(bytes) != file.Crc) throw new InvalidDataException("Die ZIP-Prüfsumme der Definitionsdatei stimmt nicht überein.");
        return DecodeText(bytes);
    }

    public override void Dispose()
    {
        _archive.Dispose();
        base.Dispose();
    }

    private static Index ReadIndex(FileStream stream)
    {
        // Bound allocation before ZipArchive eagerly loads the directory. ZIP64 and split archives remain unsupported.
        var tailSize = (int)Math.Min(stream.Length, 22 + ushort.MaxValue + 14);
        if (tailSize < 22) throw InvalidIndex();
        stream.Position = stream.Length - tailSize;
        var tail = ReadExactBytes(stream, tailSize, CancellationToken.None, false);
        (uint Offset, uint Size, ushort Count)? endRecord = null;
        for (var position = tail.Length - 22; position >= 0; position--)
        {
            var end = tail.AsSpan(position);
            if (U32(end, 0) != 0x06054b50) continue;
            var comment = U16(end, 20);
            var count = U16(end, 10);
            var size = U32(end, 12);
            var offset = U32(end, 16);
            var logicalEnd = position + 22 + comment;
            if (logicalEnd != tail.Length && !IsOrphanEndTail(tail, logicalEnd, comment, count, size, offset)) continue;
            if (count == ushort.MaxValue || size == uint.MaxValue || offset == uint.MaxValue)
                throw new NotSupportedException("ZIP64-Definitionsarchive werden nicht unterstützt.");
            if (U16(end, 4) != 0 || U16(end, 6) != 0 || U16(end, 8) != count)
                throw new NotSupportedException("Mehrteilige ZIP-Archive werden nicht unterstützt.");
            if (endRecord is not null || size > MaximumMetadataBytes || (long)offset + size != stream.Length - tailSize + position)
                throw InvalidIndex();
            endRecord = (offset, size, count);
        }
        if (endRecord is not { } directory) throw InvalidIndex();
        stream.Position = directory.Offset;
        var bytes = ReadExactBytes(stream, checked((int)directory.Size), CancellationToken.None, false);
        var entries = new List<Entry>(directory.Count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var consumed = 0;
        while (consumed < bytes.Length)
        {
            var header = bytes.AsSpan(consumed);
            if (header.Length < 46 || U32(header, 0) != 0x02014b50 || entries.Count >= directory.Count || entries.Count >= MaximumFiles)
                throw InvalidIndex();
            var nameLength = U16(header, 28);
            var extraLength = U16(header, 30);
            var commentLength = U16(header, 32);
            var length = 46 + nameLength + extraLength + commentLength;
            if (length > header.Length) throw InvalidIndex();
            var version = U16(header, 6);
            var flags = U16(header, 8);
            var method = U16(header, 10);
            if (U16(header, 34) != 0) throw new NotSupportedException("Mehrteilige ZIP-Archive werden nicht unterstützt.");
            if (U32(header, 20) == uint.MaxValue || U32(header, 24) == uint.MaxValue || U32(header, 42) == uint.MaxValue)
                throw new NotSupportedException("ZIP64-Definitionsarchive werden nicht unterstützt.");
            if (version > 20 || (flags & ~0x080e) != 0 || method is not (0 or 8) || (method == 0 && (flags & 6) != 0))
                throw new NotSupportedException("ZIP-Verschlüsselung, Kompressionsart oder Formatmerkmale werden nicht unterstützt.");
            var rawName = header.Slice(46, nameLength).ToArray();
            var name = DecodeName(rawName, flags);
            if (name.StartsWith('/')) throw new InvalidDataException("Absoluter Pfad im ZIP-Archiv.");
            var isDirectory = name.EndsWith('/');
            var path = ValidatePath("/" + name, isDirectory);
            if (!names.Add(path)) throw new InvalidDataException("Doppelter Pfad im ZIP-Archiv.");
            var attributes = U32(header, 38);
            if (((attributes >> 16) & 0xf000) == 0xa000 || (attributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Verknüpfung im ZIP-Archiv.");
            ValidateExtra(header.Slice(46 + nameLength, extraLength), rawName, name);
            if (isDirectory && (U32(header, 24) != 0 || U32(header, 16) != 0)) throw InvalidIndex();
            if (method == 0 && U32(header, 20) != U32(header, 24)) throw InvalidIndex();
            entries.Add(new Entry(name, path, rawName, isDirectory, version, flags, method,
                U32(header, 16), U32(header, 20), U32(header, 24), U32(header, 42)));
            consumed += length;
        }
        if (entries.Count != directory.Count) throw InvalidIndex();
        return new Index(directory.Offset, entries);
    }

    private static bool IsOrphanEndTail(byte[] tail, int start, ushort comment, ushort count, uint size, uint offset)
    {
        // A narrowly observed writer artifact: only the final 14 EOCD fields repeated, with its offset shifted by 14.
        // No generic trailing garbage is accepted; complete central/local coverage is verified separately below.
        if (comment != 0 || count == 0 || start != tail.Length - 14 || offset > uint.MaxValue - 14) return false;
        var orphan = tail.AsSpan(start);
        return U16(orphan, 0) == count && U16(orphan, 2) == count && U32(orphan, 4) == size &&
               U32(orphan, 8) == offset + 14 && U16(orphan, 12) == 0;
    }

    private static void ValidateLocalRecords(FileStream stream, Index index)
    {
        var ordered = index.Entries.OrderBy(entry => entry.Offset).ToArray();
        long expectedOffset = 0;
        Span<byte> header = stackalloc byte[30];
        Span<byte> descriptor = stackalloc byte[16];
        for (var i = 0; i < ordered.Length; i++)
        {
            var entry = ordered[i];
            if (entry.Offset != expectedOffset || (long)entry.Offset + header.Length > index.Offset) throw InvalidIndex();
            stream.Position = entry.Offset;
            stream.ReadExactly(header);
            if (U32(header, 0) != 0x04034b50 || U16(header, 4) != entry.Version || U16(header, 6) != entry.Flags || U16(header, 8) != entry.Method)
                throw InvalidIndex();
            var nameLength = U16(header, 26);
            var extraLength = U16(header, 28);
            var dataOffset = (long)entry.Offset + 30 + nameLength + extraLength;
            var dataEnd = dataOffset + entry.CompressedLength;
            var nextOffset = i + 1 < ordered.Length ? ordered[i + 1].Offset : index.Offset;
            if (nameLength != entry.RawName.Length || dataEnd > nextOffset) throw InvalidIndex();
            var variable = ReadExactBytes(stream, nameLength + extraLength, CancellationToken.None, false);
            if (!variable.AsSpan(0, nameLength).SequenceEqual(entry.RawName)) throw InvalidIndex();
            ValidateExtra(variable.AsSpan(nameLength), entry.RawName, entry.Name);
            var hasDescriptor = (entry.Flags & 8) != 0;
            if (!MatchesLocalValue(U32(header, 14), entry.Crc, hasDescriptor) ||
                !MatchesLocalValue(U32(header, 18), entry.CompressedLength, hasDescriptor) ||
                !MatchesLocalValue(U32(header, 22), entry.Length, hasDescriptor)) throw InvalidIndex();
            if (hasDescriptor)
            {
                var descriptorSize = nextOffset - dataEnd;
                if (descriptorSize is not (12 or 16)) throw InvalidIndex();
                stream.Position = dataEnd;
                var data = descriptor[..(int)descriptorSize];
                stream.ReadExactly(data);
                if (descriptorSize == 16)
                {
                    if (U32(data, 0) != 0x08074b50) throw InvalidIndex();
                    data = data[4..];
                }
                if (U32(data, 0) != entry.Crc || U32(data, 4) != entry.CompressedLength || U32(data, 8) != entry.Length) throw InvalidIndex();
            }
            else if (dataEnd != nextOffset) throw InvalidIndex();
            expectedOffset = nextOffset;
        }
        if (expectedOffset != index.Offset) throw InvalidIndex();
    }

    private static bool MatchesLocalValue(uint local, uint central, bool descriptor) => local == central || (descriptor && local == 0);

    private static string DecodeName(byte[] bytes, ushort flags)
    {
        try { return ((flags & 0x800) != 0 ? StrictUtf8 : LegacyEncoding).GetString(bytes); }
        catch (DecoderFallbackException exception) { throw new InvalidDataException("Ungültige UTF-8-Pfadkodierung im ZIP-Archiv.", exception); }
    }

    private static void ValidateExtra(ReadOnlySpan<byte> bytes, byte[] rawName, string name)
    {
        var seen = new HashSet<ushort>();
        while (!bytes.IsEmpty)
        {
            if (bytes.Length < 4) throw InvalidIndex();
            var type = U16(bytes, 0);
            var length = U16(bytes, 2);
            if (length > bytes.Length - 4 || !seen.Add(type)) throw InvalidIndex();
            var value = bytes.Slice(4, length);
            switch (type)
            {
                case 0x7075: // Info-ZIP Unicode Path: it must not create a second name for the same record.
                    if (value.Length < 5 || value[0] != 1 || U32(value, 1) != Crc32(rawName)) throw InvalidIndex();
                    try { if (StrictUtf8.GetString(value[5..]) != name) throw InvalidIndex(); }
                    catch (DecoderFallbackException exception) { throw new InvalidDataException("Ungültiger Unicode-Pfad im ZIP-Archiv.", exception); }
                    break;
                // Timestamp, UID/GID and comment-only metadata cannot override file paths, sizes or compression.
                case 0x000a: // NTFS timestamps
                case 0x5455: // Extended timestamps
                case 0x5855: // Original Info-ZIP Unix attributes
                case 0x7855: // Info-ZIP Unix UID/GID
                case 0x7875: // New Info-ZIP Unix UID/GID
                case 0x6375: // Unicode comment
                    break;
                default:
                    throw new NotSupportedException("Zusätzliche ZIP-Formatmerkmale werden nicht unterstützt.");
            }
            bytes = bytes[(4 + length)..];
        }
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        return ~crc;
    }

    private static ushort U16(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
    private static uint U32(ReadOnlySpan<byte> bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    private static InvalidDataException InvalidIndex() => new("Der ZIP-Verzeichnisindex und die lokalen Dateieinträge sind unvollständig oder widersprüchlich.");
    private sealed record Index(uint Offset, IReadOnlyList<Entry> Entries);
    private sealed record Entry(string Name, string Path, byte[] RawName, bool Directory, ushort Version, ushort Flags,
        ushort Method, uint Crc, uint CompressedLength, uint Length, uint Offset);
}
