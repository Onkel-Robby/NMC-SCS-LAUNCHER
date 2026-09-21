using System.Security.Cryptography;
using System.Text;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsSaveEditService : IScsSaveEditService
{
    private static readonly HashSet<string> AllowedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "game.sii",
        "profile.sii"
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly IScsSaveCodec _codec;
    private readonly IScsGameProcessGuard _processGuard;
    private readonly string _backupRoot;

    public ScsSaveEditService(
        IScsSaveCodec codec,
        IScsGameProcessGuard processGuard,
        string? backupRoot = null)
    {
        _codec = codec;
        _processGuard = processGuard;
        _backupRoot = Path.GetFullPath(backupRoot ?? AppPaths.SaveEditorBackupsDirectory);
    }

    public async Task<ScsSaveEditResult> ApplyAsync(
        ScsSaveEditRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureGameStopped();

        var targetPath = ValidateTargetPath(request.FilePath);
        var operation = string.IsNullOrWhiteSpace(request.Operation)
            ? "Save edit"
            : request.Operation.Trim();

        var source = await _codec.ReadAsync(targetPath, cancellationToken);
        _ = ScsSiiTextDocument.Parse(request.UpdatedContent);

        Directory.CreateDirectory(_backupRoot);
        var backupDirectory = Path.Combine(
            _backupRoot,
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff"),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDirectory);

        var backupPath = Path.Combine(backupDirectory, Path.GetFileName(targetPath));
        File.Copy(targetPath, backupPath, overwrite: false);

        var originalSha256 = await ComputeSha256Async(targetPath, cancellationToken);
        var tempPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".nmc-save-edit-{Guid.NewGuid():N}.tmp");
        var rollbackPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".nmc-save-rollback-{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(
                tempPath,
                request.UpdatedContent,
                Utf8NoBom,
                cancellationToken);

            _ = await _codec.ReadAsync(tempPath, cancellationToken);

            File.Replace(tempPath, targetPath, rollbackPath, ignoreMetadataErrors: true);

            var updatedSha256 = await ComputeSha256Async(targetPath, cancellationToken);
            return new ScsSaveEditResult(
                targetPath,
                backupPath,
                operation,
                originalSha256,
                updatedSha256,
                DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is not ScsSaveEditException)
        {
            throw new ScsSaveEditException(
                $"SII-Änderung '{operation}' konnte nicht sicher angewendet werden.", ex);
        }
        finally
        {
            TryDelete(tempPath);
            TryDelete(rollbackPath);
        }
    }

    public async Task RestoreBackupAsync(
        string backupPath,
        string targetFilePath,
        CancellationToken cancellationToken = default)
    {
        EnsureGameStopped();

        var fullBackupPath = Path.GetFullPath(backupPath);
        if (!IsWithinRoot(fullBackupPath, _backupRoot))
            throw new ScsSaveEditException("Das Backup liegt nicht im verwalteten NMC-Save-Backupbereich.");

        if (!File.Exists(fullBackupPath))
            throw new ScsSaveEditException("Das ausgewählte Save-Backup wurde nicht gefunden.");

        var targetPath = ValidateTargetPath(targetFilePath);
        _ = await _codec.ReadAsync(fullBackupPath, cancellationToken);

        var tempPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".nmc-save-restore-{Guid.NewGuid():N}.tmp");
        var rollbackPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".nmc-save-restore-rollback-{Guid.NewGuid():N}.tmp");

        try
        {
            File.Copy(fullBackupPath, tempPath, overwrite: false);
            _ = await _codec.ReadAsync(tempPath, cancellationToken);
            File.Replace(tempPath, targetPath, rollbackPath, ignoreMetadataErrors: true);
        }
        catch (Exception ex) when (ex is not ScsSaveEditException)
        {
            throw new ScsSaveEditException("Das Save-Backup konnte nicht sicher wiederhergestellt werden.", ex);
        }
        finally
        {
            TryDelete(tempPath);
            TryDelete(rollbackPath);
        }
    }

    private void EnsureGameStopped()
    {
        if (_processGuard.IsAnyScsGameRunning())
            throw new ScsSaveEditException(
                "ETS2 oder ATS läuft noch. Save-Dateien werden nur bei beendetem Spiel verändert.");
    }

    private static string ValidateTargetPath(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        if (!AllowedFileNames.Contains(Path.GetFileName(fullPath)))
            throw new ScsSaveEditException("Es dürfen nur game.sii und profile.sii bearbeitet werden.");

        if (!File.Exists(fullPath))
            throw new ScsSaveEditException($"SII-Datei wurde nicht gefunden: {fullPath}");

        return fullPath;
    }

    private static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static bool IsWithinRoot(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup can be retried later; never hide the primary operation result.
        }
    }
}
