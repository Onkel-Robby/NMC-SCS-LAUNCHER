using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsSaveCodecTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "NmcScsLauncher-CodecTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReadAsync_UsesPlainTextCodecWithoutDecoderForTextSave()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "game.sii");
        await File.WriteAllTextAsync(
            path,
            "SiiNunit\n{\n money_account: 10\n}\n");

        var decoder = new FakeDecoder("SiiNunit\n{\n money_account: 99\n}\n");
        var codec = new ScsSaveCodec(new ScsPlainTextSaveCodec(), decoder);

        var document = await codec.ReadAsync(path);

        Assert.Equal(ScsSaveDocumentFormat.PlainText, document.Format);
        Assert.Equal(0, decoder.CallCount);
        Assert.Contains("money_account: 10", document.Content);
    }

    [Fact]
    public async Task ReadAsync_UsesDecoderForBinarySaveAndValidatesResult()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "game.sii");
        await File.WriteAllBytesAsync(path, [0x00, 0x01, 0x02, 0x03]);

        var decoder = new FakeDecoder(
            "SiiNunit\n{\n money_account: 123\n experience_points: 456\n}\n");
        var codec = new ScsSaveCodec(new ScsPlainTextSaveCodec(), decoder);

        var document = await codec.ReadAsync(path);

        Assert.Equal(ScsSaveDocumentFormat.DecodedText, document.Format);
        Assert.Equal(1, decoder.CallCount);
        Assert.Contains("money_account: 123", document.Content);
    }

    [Fact]
    public async Task ReadAsync_FailsClosedWhenDecoderReturnsInvalidText()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "game.sii");
        await File.WriteAllBytesAsync(path, [0x00, 0x01, 0x02, 0x03]);

        var codec = new ScsSaveCodec(
            new ScsPlainTextSaveCodec(),
            new FakeDecoder("not a valid save"));

        await Assert.ThrowsAsync<ScsSaveEditException>(() =>
            codec.ReadAsync(path));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class FakeDecoder : IScsSaveDecoder
    {
        private readonly string _result;

        public FakeDecoder(string result)
        {
            _result = result;
        }

        public int CallCount { get; private set; }

        public Task<string> DecodeToTextAsync(
            string filePath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_result);
        }
    }
}
