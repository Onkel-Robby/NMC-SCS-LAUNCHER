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

        var targetPath = ValidateTargetPath(request.FilePath);
        EnsureWriteAllowed(
            targetPath,
            request.WriteMode,
            request.ExpectedLastWriteTimeUtc);
        ValidateExpectedLastWriteTime(
            targetPath,
            request.ExpectedLastWriteTimeUtc);

        var operation = string.IsNullOrWhiteSpace(request.Operation)
            ? "Save edit"
            : request.Operation.Trim();

        _ = await _codec.ReadAsync(targetPath, cancellationToken);
        ValidateExpectedLastWriteTime(
            targetPath,
            request.ExpectedLastWriteTimeUtc);
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

            ValidateExpectedLastWriteTime(
                targetPath,
                request.ExpectedLastWriteTimeUtc);
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
        ScsSaveWriteMode writeMode = ScsSaveWriteMode.RequireGameStopped,
        DateTimeOffset? expectedLastWriteTimeUtc = null,
        CancellationToken cancellationToken = default)
    {
        var fullBackupPath = Path.GetFullPath(backupPath);
        if (!IsWithinRoot(fullBackupPath, _backupRoot))
            throw new ScsSaveEditException("Das Backup liegt nicht im verwalteten NMC-Save-Backupbereich.");

        if (!File.Exists(fullBackupPath))
            throw new ScsSaveEditException("Das ausgewählte Save-Backup wurde nicht gefunden.");

        var targetPath = ValidateTargetPath(targetFilePath);
        EnsureWriteAllowed(targetPath, writeMode, expectedLastWriteTimeUtc);
        ValidateExpectedLastWriteTime(targetPath, expectedLastWriteTimeUtc);
        _ = await _codec.ReadAsync(fullBackupPath, cancellationToken);
        ValidateExpectedLastWriteTime(targetPath, expectedLastWriteTimeUtc);

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
            ValidateExpectedLastWriteTime(targetPath, expectedLastWriteTimeUtc);
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

    private void EnsureWriteAllowed(
        string targetPath,
        ScsSaveWriteMode writeMode,
        DateTimeOffset? expectedLastWriteTimeUtc)
    {
        if (!_processGuard.IsAnyScsGameRunning())
            return;

        var fileName = Path.GetFileName(targetPath);
        if (writeMode == ScsSaveWriteMode.AllowWhileGameRunning &&
            fileName.Equals("game.sii", StringComparison.OrdinalIgnoreCase))
        {
            if (expectedLastWriteTimeUtc is null)
            {
                throw new ScsSaveEditException(
                    "Bearbeiten bei laufendem ETS2/ATS erfordert einen aktuellen Save-Dateistand. " +
                    "Bitte den Save im Launcher neu laden.");
            }

            return;
        }

        if (fileName.Equals("profile.sii", StringComparison.OrdinalIgnoreCase))
        {
            throw new ScsSaveEditException(
                "Profiländerungen werden nur bei vollständig beendetem ETS2/ATS durchgeführt.");
        }

        throw new ScsSaveEditException(
            "Dieser Save-Typ wird bei laufendem ETS2/ATS nicht verändert. " +
            "Quicksaves und manuelle Saves können direkt bearbeitet werden.");
    }

    private static void ValidateExpectedLastWriteTime(
        string targetPath,
        DateTimeOffset? expectedLastWriteTimeUtc)
    {
        if (expectedLastWriteTimeUtc is null)
            return;

        var actualUtc = File.GetLastWriteTimeUtc(targetPath);
        if (actualUtc.Ticks != expectedLastWriteTimeUtc.Value.UtcDateTime.Ticks)
        {
            throw new ScsSaveEditException(
                "Die Save-Datei wurde seit dem Einlesen verändert. " +
                "Der Schreibvorgang wurde abgebrochen; bitte den Save neu laden.");
        }
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
