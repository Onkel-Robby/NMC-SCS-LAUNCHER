using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class DecryptTruckSaveDecoder : IScsSaveDecoder
{
    public const string Version = "1.3.7";
    public const string ExpectedSha256 = "7d8521e482646f11ac986d97063dbe0cc8c7b98716a6fe02cf7b621c19a64e2d";

    private static readonly TimeSpan DecodeTimeout = TimeSpan.FromSeconds(30);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly string _decoderPath;

    public DecryptTruckSaveDecoder()
    {
        _decoderPath = Path.Combine(
            AppContext.BaseDirectory,
            "third-party",
            "decrypt-truck",
            "decrypt_truck_windows.exe");
    }

    public async Task<string> DecodeToTextAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var inputPath = Path.GetFullPath(filePath);
        if (!File.Exists(inputPath))
            throw new ScsSaveEditException($"SII-Datei wurde nicht gefunden: {inputPath}");

        if (!File.Exists(_decoderPath))
        {
            throw new ScsSaveEditException(
                "Der SCS-Save-Decoder ist in dieser Installation nicht vorhanden. " +
                "Die Save-Datei wurde nicht verändert.");
        }

        await VerifyDecoderIntegrityAsync(cancellationToken);

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "NmcScsLauncher",
            "scs-decoder",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        var outputPath = Path.Combine(tempDirectory, "decoded.sii");

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _decoderPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            process.StartInfo.ArgumentList.Add(inputPath);
            process.StartInfo.ArgumentList.Add(outputPath);

            if (!process.Start())
                throw new ScsSaveEditException("Der SCS-Save-Decoder konnte nicht gestartet werden.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DecodeTimeout);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                throw new ScsSaveEditException(
                    $"Der SCS-Save-Decoder hat nach {DecodeTimeout.TotalSeconds:0} Sekunden nicht geantwortet.");
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                throw;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!File.Exists(outputPath))
            {
                var detail = string.IsNullOrWhiteSpace(stderr)
                    ? string.IsNullOrWhiteSpace(stdout) ? "keine Diagnose" : stdout.Trim()
                    : stderr.Trim();

                throw new ScsSaveEditException(
                    $"Der SCS-Save-Decoder hat keine Ausgabedatei erzeugt ({detail}).");
            }

            var bytes = await File.ReadAllBytesAsync(outputPath, cancellationToken);
            string decoded;
            try
            {
                decoded = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException ex)
            {
                throw new ScsSaveEditException(
                    "Der SCS-Save-Decoder hat keine gültige UTF-8-SII-Datei erzeugt.", ex);
            }

            _ = ScsSiiTextDocument.Parse(decoded);
            return decoded;
        }
        finally
        {
            TryDeleteDirectory(tempDirectory);
        }
    }

    private async Task VerifyDecoderIntegrityAsync(CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            _decoderPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();

        if (!string.Equals(actual, ExpectedSha256, StringComparison.Ordinal))
        {
            throw new ScsSaveEditException(
                "Die Integritätsprüfung des SCS-Save-Decoders ist fehlgeschlagen. " +
                "Der Decoder wird nicht ausgeführt.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
