using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsDefinitionSourceTests : IDisposable
{
    private const string Definition = "/def/vehicle/truck/test.model/engine/test.sii";
    private const string Text = "SiiNunit\n{\naccessory_engine_data : test.test.engine { name: \"Motor ä\" }\n}\n";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "NmcDefinitionSources", Guid.NewGuid().ToString("N"));

    public ScsDefinitionSourceTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("folder")]
    [InlineData("zip")]
    [InlineData("v1")]
    [InlineData("v2")]
    [InlineData("v1stored")]
    [InlineData("v2stored")]
    public void ReadsActualDefinitionsAndOnlyRequestedSubtree(string kind)
    {
        var path = CreateSource(kind);
        using var source = ScsDefinitionSource.Open(path);
        Assert.Equal([Definition], source.GetFiles("/def/vehicle/truck"));
        Assert.Equal(Text, source.ReadText(Definition));
        Assert.Equal("name: \"Shared\"", source.ReadText("/def/include.sui"));
        Assert.Empty(source.GetFiles("/missing"));
        Assert.Empty(source.GetFiles("/def/vehicle/truck/test"));
    }

    [Theory]
    [InlineData("folder")]
    [InlineData("zip")]
    [InlineData("v1")]
    [InlineData("v2")]
    public void RejectsUnsafeVirtualPathsAndCancellation(string kind)
    {
        using var source = ScsDefinitionSource.Open(CreateSource(kind));
        foreach (var path in new[] { "/../outside.sii", "/def/../outside.sii", "/def\\include.sui", "C:/outside", "//def/include.sui", "/def/include.sui:stream", "/def/./include.sui" })
            Assert.Throws<InvalidDataException>(() => source.ReadText(path));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => source.GetFiles("/def", cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => source.ReadText(Definition, cancellation.Token));
    }

    [Theory]
    [InlineData("folder")]
    [InlineData("zip")]
    [InlineData("v1")]
    [InlineData("v2")]
    public void RejectsMalformedUtf8AndAllowsBom(string kind)
    {
        using (var source = ScsDefinitionSource.Open(CreateSource(kind, [0xc3, 0x28])))
            Assert.Throws<InvalidDataException>(() => source.ReadText(Definition));
        using var bomSource = ScsDefinitionSource.Open(CreateSource(kind, [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(Text)]));
        Assert.Equal(Text, bomSource.ReadText(Definition));
    }

    [Theory]
    [InlineData("folder")]
    [InlineData("zip")]
    [InlineData("v1")]
    [InlineData("v2")]
    public void RejectsOversizedTextBeforeReading(string kind)
    {
        using var source = ScsDefinitionSource.Open(CreateSource(kind, new byte[4 * 1024 * 1024 + 1]));
        Assert.Throws<InvalidDataException>(() => source.ReadText(Definition));
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public void RejectsTruncatedHashFsAndExcessiveTableCount(string kind)
    {
        var path = CreateSource(kind);
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes[..18]);
        Assert.ThrowsAny<IOException>(() => ScsDefinitionSource.Open(path));
        BitConverter.GetBytes(uint.MaxValue).CopyTo(bytes, 12);
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(path));
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public void UsesActualDirectoryEntryNotPrefixGuessing(string kind)
    {
        using var source = ScsDefinitionSource.Open(CreateSource(kind, badListing: true));
        Assert.Throws<InvalidDataException>(() => source.GetFiles("/def/vehicle/truck"));
    }

    [Theory]
    [InlineData("v1")]
    [InlineData("v2")]
    public void RejectsDuplicateHashes(string kind)
    {
        var path = CreateHashFs(kind, duplicateHash: true);
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(path));
    }

    [Theory]
    [InlineData("../outside.sii")]
    [InlineData("/absolute.sii")]
    [InlineData("def\\evil.sii")]
    public void RejectsUnsafeZipEntries(string entry)
    {
        var path = Path.Combine(_root, Guid.NewGuid() + ".scs");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            zip.CreateEntry(entry);
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(path));
    }

    [Fact]
    public void RejectsDuplicateZipPaths()
    {
        var path = Path.Combine(_root, "duplicate.scs");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            zip.CreateEntry("def/one.sii");
            zip.CreateEntry("def/ONE.sii");
        }
        Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(path));
    }

    [Fact]
    public void RejectsWindowsDeviceNamesBeforeAnyLookup()
    {
        using var source = ScsDefinitionSource.Open(CreateSource("zip"));
        foreach (var name in new[] { "CON.sii", "aux", "NUL.txt", "LPT1.sui", "COM9", "COM¹", "CONIN$" })
            Assert.Throws<InvalidDataException>(() => source.ReadText("/def/" + name));
    }

    [Fact]
    public void RejectsUnknownVersionAndZip64()
    {
        var path = CreateSource("v2");
        var bytes = File.ReadAllBytes(path);
        bytes[4] = 3;
        File.WriteAllBytes(path, bytes);
        Assert.Throws<NotSupportedException>(() => ScsDefinitionSource.Open(path));
        var zipPath = CreateSource("zip");
        bytes = File.ReadAllBytes(zipPath);
        bytes[^14] = 0xff;
        bytes[^13] = 0xff;
        File.WriteAllBytes(zipPath, bytes);
        Assert.Throws<NotSupportedException>(() => ScsDefinitionSource.Open(zipPath));
    }

    [Fact]
    public void RejectsFolderReparsePointsIncludingAncestors()
    {
        var sourceRoot = CreateSource("folder");
        var link = Path.Combine(_root, "linked");
        if (OperatingSystem.IsWindows())
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                Arguments = $"/c mklink /J \"{link}\" \"{sourceRoot}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            Assert.NotNull(process);
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        else
            Directory.CreateSymbolicLink(link, sourceRoot);
        try
        {
            Assert.Throws<InvalidDataException>(() => ScsDefinitionSource.Open(Path.Combine(link, "def")));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public void CumulativeReadBudgetStopsRepeatedExpansion()
    {
        using var source = ScsDefinitionSource.Open(CreateSource("zip", Encoding.UTF8.GetBytes(new string('x', 4 * 1024 * 1024))));
        for (var i = 0; i < 16; i++)
            Assert.Equal(4 * 1024 * 1024, source.ReadText(Definition).Length);
        Assert.Throws<InvalidDataException>(() => source.ReadText(Definition));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadsPayloadAndIndexBeyondFourGiB(bool v2)
    {
        var path = Path.Combine(_root, "large.scs");
        const ulong payloadOffset = 0x100000040;
        const ulong tableOffset = 0x100001000;
        var text = Encoding.UTF8.GetBytes(Text);
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            if (OperatingSystem.IsWindows())
                Assert.True(DeviceIoControl(file.SafeFileHandle, 0x000900c4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero), "Unable to create sparse test fixture.");
            using var writer = new BinaryWriter(file, Encoding.UTF8, true);
            file.Position = (long)payloadOffset;
            writer.Write(text);
            file.Position = (long)tableOffset;
            using var table = new MemoryStream();
            using var tw = new BinaryWriter(table);
            tw.Write(0x19017ad3623342ceUL);
            byte[]? compressedMetadata = null;
            if (v2)
            {
                tw.Write(0u);
                tw.Write((ushort)1);
                tw.Write((ushort)0);
                using var metadata = new MemoryStream();
                using var mw = new BinaryWriter(metadata);
                mw.Write(0x80000001u);
                mw.Write((uint)text.Length);
                mw.Write((uint)text.Length);
                mw.Write(0u);
                mw.Write((uint)(payloadOffset / 16));
                compressedMetadata = Compress(metadata.ToArray());
            }
            else
            {
                tw.Write(payloadOffset);
                tw.Write(0u);
                tw.Write(Crc32(text));
                tw.Write((uint)text.Length);
                tw.Write((uint)text.Length);
            }
            var tableBytes = v2 ? Compress(table.ToArray()) : table.ToArray();
            writer.Write(tableBytes);
            var metadataOffset = file.Position;
            if (v2) writer.Write(compressedMetadata!);
            file.Position = 0;
            writer.Write("SCS#"u8);
            writer.Write((ushort)(v2 ? 2 : 1));
            writer.Write((ushort)0);
            writer.Write("CITY"u8);
            writer.Write(1u);
            if (v2)
            {
                writer.Write((uint)tableBytes.Length);
                writer.Write(5u);
                writer.Write((uint)compressedMetadata!.Length);
                writer.Write(tableOffset);
                writer.Write((ulong)metadataOffset);
                writer.Write(0UL);
            }
            else writer.Write(tableOffset);
        }
        using var source = ScsDefinitionSource.Open(path);
        Assert.True(new FileInfo(path).Length > uint.MaxValue);
        Assert.Equal(Text, source.ReadText(Definition));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputSize,
        IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private string CreateSource(string kind, byte[]? definition = null, bool badListing = false)
    {
        if (kind.StartsWith("v", StringComparison.Ordinal)) return CreateHashFs(kind, definition, badListing);
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        var entries = new Dictionary<string, byte[]>
        {
            [Definition.TrimStart('/')] = definition ?? Encoding.UTF8.GetBytes(Text),
            ["def/include.sui"] = Encoding.UTF8.GetBytes("name: \"Shared\""),
            ["unrelated/file.txt"] = Encoding.UTF8.GetBytes("unrelated")
        };
        if (kind == "folder")
        {
            foreach (var entry in entries)
            {
                var file = Path.Combine(path, entry.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllBytes(file, entry.Value);
            }
            return path;
        }
        path += ".scs";
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            using var stream = zip.CreateEntry(entry.Key).Open();
            stream.Write(entry.Value);
        }
        return path;
    }

    // Independent layout fixtures: fixed path hashes, not the production archive writer/reader.
    private string CreateHashFs(string kind, byte[]? definition = null, bool badListing = false, bool duplicateHash = false)
    {
        var v2 = kind.StartsWith("v2", StringComparison.Ordinal);
        var compressed = !kind.EndsWith("stored", StringComparison.Ordinal);
        var records = new List<(ulong Hash, bool Directory, byte[] Data)>
        {
            (0x9ae16a3b2f90404f, true, Listing(v2, "/def", "/unrelated")),
            (0x2c6f469efb31c45a, true, Listing(v2, "/vehicle", "include.sui")),
            (0x24fdaf288cc01f62, true, Listing(v2, "/truck")),
            (0x8e71097578bca5e6, true, Listing(v2, "/test.model")),
            (0xddd3745968879593, true, Listing(v2, "/engine")),
            (0xff22fef305b9031f, true, Listing(v2, badListing ? "../evil.sii" : "test.sii")),
            (0x19017ad3623342ce, false, definition ?? Encoding.UTF8.GetBytes(Text)),
            (0x4886ca5d29eb8e98, false, Encoding.UTF8.GetBytes("name: \"Shared\"")),
            (0xefe59f22556724dc, true, Listing(v2, "file.txt")),
            (0x7512224851fdddb5, false, Encoding.UTF8.GetBytes("unrelated"))
        };
        if (duplicateHash) records.Add(records[6]);
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write(new byte[64]);
        var payloads = new List<(long Offset, byte[] Bytes)>();
        foreach (var record in records)
        {
            while (output.Position % 16 != 0) writer.Write((byte)0);
            var bytes = compressed ? Compress(record.Data) : record.Data;
            payloads.Add((output.Position, bytes));
            writer.Write(bytes);
        }
        using var index = new MemoryStream();
        using var iw = new BinaryWriter(index, Encoding.UTF8, true);
        using var metadata = new MemoryStream();
        using var mw = new BinaryWriter(metadata, Encoding.UTF8, true);
        for (var i = 0; i < records.Count; i++)
        {
            var record = records[i];
            var payload = payloads[i];
            iw.Write(record.Hash);
            if (v2)
            {
                iw.Write((uint)(metadata.Position / 4));
                iw.Write((ushort)1);
                iw.Write((ushort)(record.Directory ? 1 : 0));
                var metadataWord = (uint)(metadata.Position / 4 + 1);
                mw.Write(metadataWord | ((record.Directory ? 0x81u : 0x80u) << 24));
                mw.Write((uint)payload.Bytes.Length | (compressed ? 0x10000000u : 0));
                mw.Write((uint)record.Data.Length);
                mw.Write(0u);
                mw.Write((uint)(payload.Offset / 16));
            }
            else
            {
                iw.Write((ulong)payload.Offset);
                iw.Write((record.Directory ? 1u : 0u) | (compressed ? 2u : 0u));
                iw.Write(Crc32(record.Data));
                iw.Write((uint)record.Data.Length);
                iw.Write((uint)payload.Bytes.Length);
            }
        }
        var indexOffset = output.Position;
        var indexBytes = v2 ? Compress(index.ToArray()) : index.ToArray();
        writer.Write(indexBytes);
        var metadataOffset = output.Position;
        var metadataBytes = Compress(metadata.ToArray());
        if (v2) writer.Write(metadataBytes);
        output.Position = 0;
        writer.Write("SCS#"u8);
        writer.Write((ushort)(v2 ? 2 : 1));
        writer.Write((ushort)0);
        writer.Write("CITY"u8);
        writer.Write((uint)records.Count);
        if (v2)
        {
            writer.Write((uint)indexBytes.Length);
            writer.Write((uint)(metadata.Length / 4));
            writer.Write((uint)metadataBytes.Length);
            writer.Write((ulong)indexOffset);
            writer.Write((ulong)metadataOffset);
            writer.Write(0UL);
        }
        else writer.Write((ulong)indexOffset);
        var path = Path.Combine(_root, Guid.NewGuid() + ".scs");
        File.WriteAllBytes(path, output.ToArray());
        return path;
    }

    private static byte[] Listing(bool v2, params string[] names)
    {
        if (!v2) return Encoding.UTF8.GetBytes(string.Join('\n', names.Select(name => name.StartsWith('/') ? "*" + name[1..] : name)) + "\n");
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);
        writer.Write((uint)names.Length);
        foreach (var name in names) writer.Write((byte)Encoding.UTF8.GetByteCount(name));
        foreach (var name in names) writer.Write(Encoding.UTF8.GetBytes(name));
        return output.ToArray();
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Fastest, true)) zlib.Write(data);
        return output.ToArray();
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
}
