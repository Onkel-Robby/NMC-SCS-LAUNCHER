using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using NmcScsLauncher.Infrastructure.ThirdParty;

namespace NmcScsLauncher.Infrastructure;

/// <summary>
/// Original, bounded reader for ordinary HashFS definition payloads. It does not unpack files,
/// repair modified archives, decode textures, or establish whether a source is mounted by the game.
/// </summary>
internal sealed class HashFsDefinitionSource : ScsDefinitionSource
{
    private const int MaximumEntries = 1_000_000;
    private readonly FileStream _stream;
    private readonly Dictionary<ulong, Entry> _entries = [];
    private readonly ushort _version;

    internal HashFsDefinitionSource(FileStream stream)
    {
        _stream = stream;
        Span<byte> common = stackalloc byte[16];
        stream.ReadExactly(common);
        _version = BinaryPrimitives.ReadUInt16LittleEndian(common[4..]);
        if (_version is not (1 or 2)) throw new NotSupportedException("Diese HashFS-Version wird nicht unterstützt.");
        if (BinaryPrimitives.ReadUInt16LittleEndian(common[6..]) != 0)
            throw new NotSupportedException("HashFS-Archive mit Salt werden nicht unterstützt.");
        if (!common[8..12].SequenceEqual("CITY"u8)) throw new NotSupportedException("Unbekannte HashFS-Hashfunktion.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(common[12..]);
        if (count > MaximumEntries) throw new InvalidDataException("Zu viele HashFS-Einträge.");
        if (_version == 1) ReadV1(count);
        else ReadV2(count);
    }

    public override IReadOnlyList<string> GetFiles(string virtualDirectory, CancellationToken cancellationToken = default)
    {
        CheckAvailable(cancellationToken);
        virtualDirectory = ValidatePath(virtualDirectory, directory: true);
        var root = virtualDirectory.Trim('/');
        if (!_entries.TryGetValue(HashPath(root), out var rootEntry) || !rootEntry.Directory) return [];
        var pending = new Stack<(string Path, Entry Entry, int Depth)>();
        pending.Push((root, rootEntry, 0));
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visitedDirectories = new HashSet<ulong>();
        var files = new List<string>();
        var visitedCount = 0;
        while (pending.TryPop(out var current))
        {
            CheckAvailable(cancellationToken);
            if (current.Depth > MaximumDepth) throw new InvalidDataException("HashFS-Verzeichnisse sind zu tief verschachtelt.");
            if (!visitedDirectories.Add(current.Entry.Hash)) throw new InvalidDataException("Zyklisches HashFS-Verzeichnis.");
            var listing = ReadEntry(current.Entry, cancellationToken);
            foreach (var child in ReadDirectory(listing))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++visitedCount > MaximumFiles) throw new InvalidDataException("Zu viele Einträge im HashFS-Definitionsverzeichnis.");
                ValidateSegment(child.Name);
                var relative = current.Path.Length == 0 ? child.Name : current.Path + "/" + child.Name;
                var path = ValidatePath("/" + relative);
                if (!seenPaths.Add(path)) throw new InvalidDataException("Doppelter HashFS-Definitionspfad.");
                if (!_entries.TryGetValue(HashPath(relative), out var entry) || entry.Directory != child.Directory)
                    throw new InvalidDataException("HashFS-Verzeichnis und Archivindex widersprechen sich.");
                if (child.Directory) pending.Push((relative, entry, current.Depth + 1));
                else files.Add(path);
            }
        }
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    public override string ReadText(string virtualPath, CancellationToken cancellationToken = default)
    {
        CheckAvailable(cancellationToken);
        virtualPath = ValidatePath(virtualPath);
        if (!_entries.TryGetValue(HashPath(virtualPath[1..]), out var entry) || entry.Directory)
            throw new FileNotFoundException("Definition nicht im HashFS-Archiv gefunden.", virtualPath);
        return DecodeText(ReadEntry(entry, cancellationToken));
    }

    public override void Dispose()
    {
        _stream.Dispose();
        base.Dispose();
    }

    private void ReadV1(uint count)
    {
        Span<byte> offsetBytes = stackalloc byte[8];
        _stream.ReadExactly(offsetBytes);
        var offset = BinaryPrimitives.ReadUInt64LittleEndian(offsetBytes);
        var bytes = CheckedSize((ulong)count * 32, MaximumMetadataBytes);
        ValidateRange(offset, (ulong)bytes);
        _stream.Position = (long)offset;
        var table = ReadExactBytes(_stream, bytes, CancellationToken.None, false);
        for (var i = 0; i < count; i++)
        {
            var record = table.AsSpan(i * 32, 32);
            var hash = BinaryPrimitives.ReadUInt64LittleEndian(record);
            var payloadOffset = BinaryPrimitives.ReadUInt64LittleEndian(record[8..]);
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            var checksum = BinaryPrimitives.ReadUInt32LittleEndian(record[20..]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(record[24..]);
            var packedSize = BinaryPrimitives.ReadUInt32LittleEndian(record[28..]);
            ValidateRange(payloadOffset, packedSize);
            Add(new Entry(hash, (flags & 1) != 0, payloadOffset, packedSize, size,
                (flags & 2) != 0 ? 1 : 0, (flags & ~3u) == 0, checksum));
        }
    }

    private void ReadV2(uint count)
    {
        // Common v2 fields end at byte 44. Security/platform fields are not required for data reads.
        Span<byte> rest = stackalloc byte[36];
        _stream.ReadExactly(rest);
        var packedIndexSize = BinaryPrimitives.ReadUInt32LittleEndian(rest);
        var wordCount = BinaryPrimitives.ReadUInt32LittleEndian(rest[4..]);
        var packedMetadataSize = BinaryPrimitives.ReadUInt32LittleEndian(rest[8..]);
        var indexOffset = BinaryPrimitives.ReadUInt64LittleEndian(rest[12..]);
        var metadataOffset = BinaryPrimitives.ReadUInt64LittleEndian(rest[20..]);
        var indexSize = CheckedSize((ulong)count * 16, MaximumMetadataBytes);
        var metadataSize = CheckedSize((ulong)wordCount * 4, MaximumMetadataBytes);
        if ((long)indexSize + metadataSize > MaximumMetadataBytes ||
            (ulong)packedIndexSize + packedMetadataSize > MaximumMetadataBytes)
            throw new InvalidDataException("Der HashFS-Index überschreitet das Leselimit.");
        var index = InflateTable(indexOffset, packedIndexSize, indexSize);
        var metadata = InflateTable(metadataOffset, packedMetadataSize, metadataSize);
        for (var i = 0; i < count; i++)
        {
            var record = index.AsSpan(i * 16, 16);
            var hash = BinaryPrimitives.ReadUInt64LittleEndian(record);
            var firstWord = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]);
            var descriptorCount = BinaryPrimitives.ReadUInt16LittleEndian(record[12..]);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(record[14..]);
            if (descriptorCount == 0 || descriptorCount > 128 || ((ulong)firstWord + descriptorCount) * 4 > (ulong)metadata.Length)
                throw new InvalidDataException("Ungültiger HashFS-Metadatenindex.");
            var directory = (flags & 1) != 0;
            Entry? result = null;
            // Only the documented directory/preload bits are needed for plain data.
            var unsupported = (flags & ~5u) != 0;
            for (var j = 0; j < descriptorCount; j++)
            {
                var descriptor = BinaryPrimitives.ReadUInt32LittleEndian(metadata.AsSpan(checked((int)(firstWord + j) * 4), 4));
                var type = descriptor >> 24;
                var position = (ulong)(descriptor & 0xffffff) * 4;
                if (position + 4 > (ulong)metadata.Length) throw new InvalidDataException("Metadaten zeigen außerhalb des HashFS-Index.");
                if (type is not (0x80 or 0x81))
                {
                    unsupported = true;
                    continue;
                }
                if (result is not null || position + 16 > (ulong)metadata.Length || directory != (type == 0x81))
                    throw new InvalidDataException("Mehrdeutige HashFS-Dateimetadaten.");
                var data = metadata.AsSpan((int)position, 16);
                var packedField = BinaryPrimitives.ReadUInt32LittleEndian(data);
                var unpackedField = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
                unsupported |= (unpackedField & 0xf0000000) != 0;
                var packedSize = packedField & 0x0fffffff;
                var size = unpackedField & 0x0fffffff;
                var payloadOffset = (ulong)BinaryPrimitives.ReadUInt32LittleEndian(data[12..]) * 16;
                ValidateRange(payloadOffset, packedSize);
                result = new Entry(hash, directory, payloadOffset, packedSize, size, (int)(packedField >> 28), true, null);
            }
            // Other resource metadata may describe textures/models. Keep their hashes, but do not decode them.
            Add(result is null ? new Entry(hash, directory, 0, 0, 0, -1, false, null) : result with { Supported = !unsupported });
        }
    }

    private byte[] InflateTable(ulong offset, uint packedSize, int expectedSize)
    {
        CheckedSize(packedSize, MaximumMetadataBytes);
        ValidateRange(offset, packedSize);
        _stream.Position = (long)offset;
        using var bounded = new ArchiveSliceStream(_stream, packedSize);
        using var zlib = new ZLibStream(bounded, CompressionMode.Decompress);
        return ReadExactBytes(zlib, expectedSize, CancellationToken.None, true);
    }

    private byte[] ReadEntry(Entry entry, CancellationToken token)
    {
        CheckAvailable(token);
        if (!entry.Supported || entry.Compression is not (0 or 1))
            throw new NotSupportedException("Der HashFS-Eintrag verwendet nicht unterstützte Metadaten oder Kompression.");
        CheckedSize(entry.Size, MaximumTextBytes);
        CheckedSize(entry.PackedSize, MaximumTextBytes * 2);
        if (entry.Compression == 0 && entry.Size != entry.PackedSize)
            throw new InvalidDataException("Ungültige Länge eines unkomprimierten HashFS-Eintrags.");
        _stream.Position = (long)entry.Offset;
        using var bounded = new ArchiveSliceStream(_stream, entry.PackedSize);
        byte[] bytes;
        if (entry.Compression == 1)
        {
            using var zlib = new ZLibStream(bounded, CompressionMode.Decompress);
            bytes = ReadContent(zlib, entry.Size, token);
        }
        else bytes = ReadContent(bounded, entry.Size, token);
        if (entry.Checksum is > 0 && Crc32(bytes) != entry.Checksum.Value)
            throw new InvalidDataException("Die HashFS-Prüfsumme stimmt nicht überein.");
        return bytes;
    }

    private IEnumerable<(string Name, bool Directory)> ReadDirectory(byte[] data)
    {
        if (_version == 1)
        {
            foreach (var line in DecodeText(data).Split('\n'))
            {
                var name = line.TrimEnd('\r');
                if (name.Length == 0) continue;
                var directory = name.StartsWith('*');
                yield return (directory ? name[1..] : name, directory);
            }
            yield break;
        }
        if (data.Length < 4) throw new InvalidDataException("Abgeschnittenes HashFS-Verzeichnis.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data);
        if (count > MaximumFiles || count + 4UL > (ulong)data.Length) throw new InvalidDataException("Ungültige HashFS-Verzeichnisgröße.");
        var position = checked(4 + (int)count);
        for (var i = 0; i < count; i++)
        {
            var length = data[4 + i];
            if (length == 0 || position + length > data.Length) throw new InvalidDataException("Abgeschnittener HashFS-Dateiname.");
            var name = DecodeText(data.AsSpan(position, length).ToArray());
            position += length;
            var directory = name.StartsWith('/');
            yield return (directory ? name[1..] : name, directory);
        }
        if (position != data.Length) throw new InvalidDataException("Ungültiger Rest im HashFS-Verzeichnis.");
    }

    private static ulong HashPath(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(path);
        return ScsCityHash.CityHash64(bytes, (ulong)bytes.Length);
    }

    private void Add(Entry entry)
    {
        if (!_entries.TryAdd(entry.Hash, entry)) throw new InvalidDataException("Doppelter HashFS-Pfadhash.");
    }

    private void ValidateRange(ulong offset, ulong size)
    {
        if (offset > (ulong)_stream.Length || size > (ulong)_stream.Length - offset)
            throw new InvalidDataException("Ein HashFS-Datenbereich liegt außerhalb des Archivs.");
    }

    private static int CheckedSize(ulong size, int limit)
    {
        if (size > (ulong)limit) throw new InvalidDataException("Ein HashFS-Datenbereich überschreitet das Leselimit.");
        return (int)size;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = uint.MaxValue;
        foreach (var value in data)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
        }
        return ~crc;
    }

    private sealed record Entry(ulong Hash, bool Directory, ulong Offset, uint PackedSize, uint Size, int Compression, bool Supported, uint? Checksum);

    /// <summary>Prevents a decompressor from reading beyond the selected archive payload.</summary>
    private sealed class ArchiveSliceStream(Stream input, long length) : Stream
    {
        private long _remaining = length;
        private readonly long _length = length;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _length - _remaining; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var count = input.Read(buffer[..(int)Math.Min(buffer.Length, _remaining)]);
            _remaining -= count;
            return count;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
