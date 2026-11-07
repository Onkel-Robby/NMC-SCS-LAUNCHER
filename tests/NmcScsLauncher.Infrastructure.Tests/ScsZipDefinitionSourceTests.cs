using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsZipDefinitionSourceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NmcStrictZipTests", Guid.NewGuid().ToString("N"));
    private const string Content = "SiiNunit { accessory_engine_data : test.engine { torque: 2200 } }";
    private static readonly byte[] AsciiName = "def/test.sii"u8.ToArray();

    public ScsZipDefinitionSourceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void LegacyNamesUseCp437InsteadOfAssumingUtf8OrTheWindowsCodePage()
    {
        var path = Write(Create(new Item([.. "def/sto"u8, 0xe1, .. ".sii"u8], Encoding.UTF8.GetBytes(Content))));
        using var source = ScsDefinitionSource.Open(path);
        Assert.Equal(["/def/stoß.sii"], source.GetFiles("/def"));
        Assert.Equal(Content, source.ReadText("/def/stoß.sii"));
    }

    [Fact]
    public void LegacyUnicodePathMetadataMustAgreeWithTheValidatedLegacyName()
    {
        byte[] name = [.. "def/sto"u8, 0xe1, .. ".sii"u8];
        using var source = ScsDefinitionSource.Open(Write(Create(new Item(name, Encoding.UTF8.GetBytes(Content), Extra: UnicodePath(name, "def/stoß.sii")))));
        Assert.Equal(Content, source.ReadText("/def/stoß.sii"));
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(Write(Create(new Item(name, [], Extra: UnicodePath(name, "def/different.sii"))))));
    }

    [Fact]
    public void Utf8FlagIsStrictAndSupportsRealUnicodeNames()
    {
        using var source = ScsDefinitionSource.Open(Write(Create(new Item(Encoding.UTF8.GetBytes("def/é.sii"), Encoding.UTF8.GetBytes(Content), Flags: 0x800))));
        Assert.Equal(Content, source.ReadText("/def/é.sii"));
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(Write(Create(new Item([.. "def/"u8, 0xe1, .. ".sii"u8], [], Flags: 0x800)))));
    }

    [Fact]
    public void ExactOrphanEndRecordTailIsReadableOnlyWithACompleteConsistentArchive()
    {
        var bytes = WithOrphanTail(Create(new Item(AsciiName, Encoding.UTF8.GetBytes(Content))));
        using var source = ScsDefinitionSource.Open(Write(bytes));
        Assert.Equal(Content, source.ReadText("/def/test.sii"));
        bytes[^14] ^= 1;
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(Write(bytes)));
    }

    [Fact]
    public void ArbitraryTrailingBytesAndDuplicateEndRecordsRemainRejected()
    {
        var bytes = Create(new Item(AsciiName, []));
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(Write([.. bytes, .. new byte[14]])));
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(Write([.. bytes, .. bytes[^22..]])));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void EveryOrphanTailFieldMustMatchTheSingleCompleteEndRecord(int fieldOffset)
    {
        var bytes = WithOrphanTail(Create(new Item(AsciiName, [])));
        bytes[bytes.Length - 14 + fieldOffset] ^= 1;
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(Write(bytes)));
    }

    [Fact]
    public void OrdinaryArchiveCommentsAndEmptyArchivesRemainSupported()
    {
        var bytes = Create(new Item(AsciiName, Encoding.UTF8.GetBytes(Content)));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(bytes.Length - 2), 5);
        using var commented = ScsDefinitionSource.Open(Write([.. bytes, .. "hello"u8]));
        Assert.Equal(Content, commented.ReadText("/def/test.sii"));
        using var empty = ScsDefinitionSource.Open(Write(Create()));
        Assert.Empty(empty.GetFiles("/"));
    }

    [Fact]
    public void MalformedOrContradictoryUnicodePathMetadataCannotRedefineAnEntry()
    {
        var extra = UnicodePath(AsciiName, "def/test.sii");
        extra[5] ^= 1;
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(Write(Create(new Item(AsciiName, [], Extra: extra)))));
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(Write(Create(new Item(AsciiName, [], Extra: [0x75, 0x70, 0xff, 0xff])))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PayloadCrcMustMatchForEveryRequestedRead(bool deflate)
    {
        var bytes = Create(new Item(AsciiName, Encoding.UTF8.GetBytes(Content), Method: deflate ? (ushort)8 : (ushort)0));
        var central = checked((int)U32(bytes.AsSpan(bytes.Length - 22), 16));
        // Keep both headers consistent but claim the wrong checksum; payload validation must still reject it.
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14), 0x12345678);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 16), 0x12345678);
        using var source = ScsDefinitionSource.Open(Write(bytes));
        Assert.Throws<InvalidDataException>(() => source.ReadText("/def/test.sii"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ValidDataDescriptorsRemainSupported(bool deflate, bool descriptorSignature)
    {
        using var source = ScsDefinitionSource.Open(Write(Create(new Item(AsciiName, Encoding.UTF8.GetBytes(Content),
            Flags: 8, Method: deflate ? (ushort)8 : (ushort)0, DescriptorSignature: descriptorSignature))));
        Assert.Equal(Content, source.ReadText("/def/test.sii"));
    }

    [Fact]
    public void StandardZipArchiveStreamingWriterRemainsCompatible()
    {
        using var bytes = new MemoryStream();
        using (var output = new NonSeekableStream(bytes))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        using (var stream = archive.CreateEntry("def/test.sii").Open()) stream.Write(Encoding.UTF8.GetBytes(Content));
        using var source = ScsDefinitionSource.Open(Write(bytes.ToArray()));
        Assert.Equal(Content, source.ReadText("/def/test.sii"));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("flags")]
    [InlineData("method")]
    [InlineData("crc")]
    [InlineData("size")]
    [InlineData("offset")]
    public void LocalHeadersMustAgreeWithTheCompleteCentralIndex(string mismatch)
    {
        var bytes = Create(new Item(AsciiName, Encoding.UTF8.GetBytes(Content)));
        var central = checked((int)U32(bytes.AsSpan(bytes.Length - 22), 16));
        var position = mismatch switch { "name" => 30, "flags" => 6, "method" => 8, "crc" => 14, "size" => 18, _ => central + 42 };
        bytes[position] ^= 1;
        Assert.ThrowsAny<InvalidDataException>(() => ScsDefinitionSource.Open(Write(bytes)));
    }

    [Fact]
    public void HiddenUnindexedLocalEntriesCannotBeIgnoredEvenWithTheRecognizedTail()
    {
        var bytes = Create(new Item(AsciiName, Encoding.UTF8.GetBytes(Content)), new Item("def/hidden.sii"u8.ToArray(), [], OmitFromIndex: true));
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(Write(bytes)));
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(Write(WithOrphanTail(bytes))));
    }

    [Fact]
    public void DataDescriptorChecksumMustMatchCentralIndex()
    {
        var bytes = Create(new Item(AsciiName, Encoding.UTF8.GetBytes(Content), Flags: 8, DescriptorSignature: true));
        var descriptor = 30 + AsciiName.Length + Encoding.UTF8.GetByteCount(Content);
        bytes[descriptor + 4] ^= 1;
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(Write(bytes)));
    }

    [Fact]
    public void CustomMagicEncryptedFlagsAndUnknownEncodingMetadataRemainUnsupported()
    {
        var bytes = Create(new Item(AsciiName, []));
        "AEM!"u8.CopyTo(bytes);
        Assert.Throws<NotSupportedException>(() => ScsDefinitionSource.Open(Write(bytes)));
        Assert.Throws<NotSupportedException>(() => ScsDefinitionSource.Open(Write(Create(new Item(AsciiName, [], Flags: 1)))));
        Assert.Throws<NotSupportedException>(() => ScsDefinitionSource.Open(Write(Create(new Item(AsciiName, [], Extra: [8, 0, 0, 0])))));
    }

    private string Write(byte[] bytes) { var path = Path.Combine(_root, Guid.NewGuid() + ".scs"); File.WriteAllBytes(path, bytes); return path; }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed record Item(byte[] Name, byte[] Data, ushort Flags = 0, ushort Method = 0, bool DescriptorSignature = false, byte[]? Extra = null, bool OmitFromIndex = false);

    // Independent byte-layout fixtures. No production writer or parser constructs the malformed cases.
    private static byte[] Create(params Item[] items)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        var records = new List<(Item Item, uint Offset, uint Crc, byte[] Compressed)>();
        foreach (var item in items)
        {
            var offset = (uint)output.Position; var crc = Crc(item.Data); var compressed = item.Data;
            if (item.Method == 8)
            { using var buffer = new MemoryStream(); using (var deflate = new DeflateStream(buffer, CompressionLevel.Fastest, true)) deflate.Write(item.Data); compressed = buffer.ToArray(); }
            var descriptor = (item.Flags & 8) != 0;
            writer.Write(0x04034b50u); writer.Write((ushort)20); writer.Write(item.Flags); writer.Write(item.Method);
            writer.Write(0u); writer.Write(descriptor ? 0u : crc); writer.Write(descriptor ? 0u : (uint)compressed.Length);
            writer.Write(descriptor ? 0u : (uint)item.Data.Length); writer.Write((ushort)item.Name.Length); writer.Write((ushort)(item.Extra?.Length ?? 0));
            writer.Write(item.Name); if (item.Extra is not null) writer.Write(item.Extra); writer.Write(compressed);
            if (descriptor) { if (item.DescriptorSignature) writer.Write(0x08074b50u); writer.Write(crc); writer.Write((uint)compressed.Length); writer.Write((uint)item.Data.Length); }
            if (!item.OmitFromIndex) records.Add((item, offset, crc, compressed));
        }
        var centralOffset = (uint)output.Position;
        foreach (var record in records)
        {
            var item = record.Item;
            writer.Write(0x02014b50u); writer.Write((ushort)20); writer.Write((ushort)20); writer.Write(item.Flags); writer.Write(item.Method);
            writer.Write(0u); writer.Write(record.Crc); writer.Write((uint)record.Compressed.Length); writer.Write((uint)item.Data.Length);
            writer.Write((ushort)item.Name.Length); writer.Write((ushort)(item.Extra?.Length ?? 0)); writer.Write((ushort)0);
            writer.Write((ushort)0); writer.Write((ushort)0); writer.Write(0u); writer.Write(record.Offset); writer.Write(item.Name);
            if (item.Extra is not null) writer.Write(item.Extra);
        }
        var size = (uint)output.Position - centralOffset;
        writer.Write(0x06054b50u); writer.Write((ushort)0); writer.Write((ushort)0); writer.Write((ushort)records.Count); writer.Write((ushort)records.Count);
        writer.Write(size); writer.Write(centralOffset); writer.Write((ushort)0);
        return output.ToArray();
    }
    private static byte[] WithOrphanTail(byte[] archive)
    {
        var tail = archive[^14..].ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(tail.AsSpan(8), checked(U32(tail, 8) + 14));
        return [.. archive, .. tail];
    }
    private static byte[] UnicodePath(byte[] raw, string text)
    {
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        var utf8 = Encoding.UTF8.GetBytes(text); writer.Write((ushort)0x7075); writer.Write((ushort)(5 + utf8.Length));
        writer.Write((byte)1); writer.Write(Crc(raw)); writer.Write(utf8); return bytes.ToArray();
    }
    private static uint U32(ReadOnlySpan<byte> bytes, int position) => BinaryPrimitives.ReadUInt32LittleEndian(bytes[position..]);
    private static uint Crc(byte[] bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes) { crc ^= value; for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u); }
        return ~crc;
    }
    private sealed class NonSeekableStream(Stream stream) : Stream
    {
        public override bool CanRead => false; public override bool CanWrite => true; public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => stream.Flush(); public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long value) => throw new NotSupportedException();
    }
}
