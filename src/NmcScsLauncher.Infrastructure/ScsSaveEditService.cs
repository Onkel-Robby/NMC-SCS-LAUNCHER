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
    private static readonly UTF8Encoding StrictUtf8NoBom = new(false, true);
    private const int MaximumGuardedBytes = 128 * 1024 * 1024;
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

        if (request.TransactionValidation is not null)
            return await ApplyGuardedAsync(request, cancellationToken).ConfigureAwait(false);

        var targetPath = ValidateTargetPath(request.FilePath);
        var isLiveWrite = EnsureWriteAllowed(
            targetPath,
            request.WriteMode,
            request.ExpectedLastWriteTimeUtc);
        ValidateExpectedLastWriteTime(
            targetPath,
            isLiveWrite ? request.ExpectedLastWriteTimeUtc : null);

        var operation = string.IsNullOrWhiteSpace(request.Operation)
            ? "Save edit"
            : request.Operation.Trim();

        _ = await _codec.ReadAsync(targetPath, cancellationToken);
        ValidateExpectedLastWriteTime(
            targetPath,
            isLiveWrite ? request.ExpectedLastWriteTimeUtc : null);
        var liveOriginalSha256 = isLiveWrite
            ? await ComputeSha256Async(targetPath, cancellationToken)
            : null;
        _ = ScsSiiTextDocument.Parse(request.UpdatedContent);

        Directory.CreateDirectory(_backupRoot);
        var backupDirectory = Path.Combine(
            _backupRoot,
            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff"),
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backupDirectory);

        var backupPath = Path.Combine(backupDirectory, Path.GetFileName(targetPath));
        await CopyFileAsync(targetPath, backupPath, cancellationToken);

        // Record the checksum of the bytes actually preserved in the backup.
        var originalSha256 = await ComputeSha256Async(backupPath, cancellationToken);
        if (isLiveWrite &&
            !string.Equals(originalSha256, liveOriginalSha256, StringComparison.Ordinal))
        {
            throw new ScsSaveEditException(
                "Die Save-Datei hat sich während der Backup-Erstellung verändert. " +
                "Die Änderung wurde abgebrochen; bitte den Save neu laden.");
        }
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
                isLiveWrite ? request.ExpectedLastWriteTimeUtc : null);
            await ValidateUnchangedSha256Async(
                targetPath,
                liveOriginalSha256,
                cancellationToken);
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

    private async Task<ScsSaveEditResult> ApplyGuardedAsync(ScsSaveEditRequest request, CancellationToken token)
    {
        var validation = request.TransactionValidation!;
        var targetPath = ValidateTargetPath(request.FilePath);
        var operation = string.IsNullOrWhiteSpace(request.Operation) ? "Save edit" : request.Operation.Trim();
        var tempPath = Path.Combine(Path.GetDirectoryName(targetPath)!, $".nmc-save-edit-{Guid.NewGuid():N}.tmp");
        var rollbackPath = Path.Combine(Path.GetDirectoryName(targetPath)!, $".nmc-save-rollback-{Guid.NewGuid():N}.tmp");
        string? backupPath = null;
        string? capturedOriginalSha = null;
        string? outputSha = null;
        var replaced = false;
        var removeRollback = false;
        try
        {
            token.ThrowIfCancellationRequested();
            ValidateGuardedTarget(targetPath);
            EnsureGameStopped();
            var expectedSha = validation.ExpectedOriginalSha256;
            if (expectedSha is null || expectedSha.Length != 64 || expectedSha.Any(character => !Uri.IsHexDigit(character)) ||
                validation.ValidateBeforeCommitAsync is null || validation.ValidateWrittenAsync is null)
                throw new ScsSaveEditException("Die verifizierte Transaktion benötigt einen gültigen Original-SHA-256 und beide Prüfungen.");
            await RequireGuardedHashAsync(targetPath, expectedSha, token).ConfigureAwait(false);
            if (request.UpdatedContent.Length > MaximumGuardedBytes || StrictUtf8NoBom.GetByteCount(request.UpdatedContent) > MaximumGuardedBytes)
                throw new ScsSaveEditException("Der geänderte Save überschreitet das Prüflimit von 128 MiB.");
            _ = ScsSiiTextDocument.Parse(request.UpdatedContent);
            var output = StrictUtf8NoBom.GetBytes(request.UpdatedContent);
            outputSha = Convert.ToHexString(SHA256.HashData(output));

            await validation.ValidateBeforeCommitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            ValidateGuardedTarget(targetPath);
            EnsureGameStopped();
            await RequireGuardedHashAsync(targetPath, expectedSha, token).ConfigureAwait(false);
            EnsureGameStopped();
            CheckExistingAncestry(_backupRoot);
            var backupDirectory = Path.Combine(_backupRoot, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff"), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backupDirectory);
            ScsDefinitionSource.CheckNoReparsePoints(backupDirectory);
            backupPath = Path.Combine(backupDirectory, Path.GetFileName(targetPath));
            await CopyGuardedFileAsync(targetPath, backupPath, token).ConfigureAwait(false);
            await RequireGuardedHashAsync(backupPath, expectedSha, token).ConfigureAwait(false);

            await using (var temporary = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                await temporary.WriteAsync(output, token).ConfigureAwait(false);
                await temporary.FlushAsync(token).ConfigureAwait(false);
                temporary.Flush(flushToDisk: true);
            }
            var prepared = await ReadGuardedTextAsync(tempPath, outputSha, token).ConfigureAwait(false);
            if (!string.Equals(prepared, request.UpdatedContent, StringComparison.Ordinal))
                throw new ScsSaveEditException("Die vorbereiteten Save-Bytes stimmen nicht mit der geprüften Änderung überein.");

            await validation.ValidateBeforeCommitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            ValidateGuardedTarget(targetPath);
            EnsureGameStopped();
            await RequireGuardedHashAsync(backupPath, expectedSha, token).ConfigureAwait(false);
            await RequireGuardedHashAsync(tempPath, outputSha, token).ConfigureAwait(false);
            await RequireGuardedHashAsync(targetPath, expectedSha, token).ConfigureAwait(false);
            EnsureGameStopped();
            ValidateGuardedTarget(targetPath);
            token.ThrowIfCancellationRequested();
            // A single atomic replacement, with no copy/delete fallback. Its captured preimage
            // closes the last hash-to-replace race: a newer preimage must be restored, not discarded.
            File.Replace(tempPath, targetPath, rollbackPath, ignoreMetadataErrors: true);
            replaced = true;
            capturedOriginalSha = await GuardedHashAsync(rollbackPath, CancellationToken.None).ConfigureAwait(false);
            if (!HashEquals(capturedOriginalSha, expectedSha))
                throw new ScsSaveEditException("Die tatsächlich ersetzte Save-Datei war bereits neuer als der geprüfte Originalstand.");
            var written = await ReadGuardedTextAsync(targetPath, outputSha, token).ConfigureAwait(false);
            await validation.ValidateWrittenAsync(written, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            ValidateGuardedTarget(targetPath);
            await RequireGuardedHashAsync(backupPath, expectedSha, token).ConfigureAwait(false);
            await RequireGuardedHashAsync(targetPath, outputSha, token).ConfigureAwait(false);
            EnsureGameStopped();
            token.ThrowIfCancellationRequested();
            removeRollback = true;
            return new(targetPath, backupPath, operation, expectedSha.ToLowerInvariant(), outputSha.ToLowerInvariant(), DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            // ReplaceFile can fail after producing a rollback file. Treat that as a possible
            // completed replacement and preserve/recover its actual captured bytes as well.
            if ((replaced || File.Exists(rollbackPath)) && outputSha is not null)
            {
                try { capturedOriginalSha ??= await GuardedHashAsync(rollbackPath, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception captureFailure) when (captureFailure is IOException or UnauthorizedAccessException or ScsSaveEditException or InvalidDataException)
                {
                    throw RecoveryFailure(operation, backupPath, rollbackPath, "Der erfasste Originalstand konnte nicht geprüft werden.", exception);
                }
                var recoveryError = await TryRecoverGuardedAsync(targetPath, rollbackPath, capturedOriginalSha, outputSha).ConfigureAwait(false);
                if (recoveryError is not null)
                    throw RecoveryFailure(operation, backupPath, rollbackPath, recoveryError, exception);
                removeRollback = true;
                if (exception is OperationCanceledException) throw;
                throw new ScsSaveEditException($"SII-Änderung '{operation}' wurde abgebrochen; der tatsächlich ersetzte Dateistand wurde atomar wiederhergestellt. Backup: {backupPath}", exception);
            }
            if (exception is ScsSaveEditException or OperationCanceledException) throw;
            throw new ScsSaveEditException($"SII-Änderung '{operation}' konnte nicht sicher angewendet werden.", exception);
        }
        finally
        {
            TryDelete(tempPath);
            if (removeRollback) TryDelete(rollbackPath);
        }
    }

    private static async Task<string?> TryRecoverGuardedAsync(string targetPath, string rollbackPath, string originalSha, string proposedSha)
    {
        var restoration = Path.Combine(Path.GetDirectoryName(targetPath)!, $".nmc-save-recovery-{Guid.NewGuid():N}.tmp");
        var displaced = Path.Combine(Path.GetDirectoryName(targetPath)!, $".nmc-save-recovery-displaced-{Guid.NewGuid():N}.tmp");
        var recovered = false;
        try
        {
            ValidateGuardedTarget(targetPath);
            await RequireGuardedHashAsync(targetPath, proposedSha, CancellationToken.None).ConfigureAwait(false);
            await RequireGuardedHashAsync(rollbackPath, originalSha, CancellationToken.None).ConfigureAwait(false);
            await CopyGuardedFileAsync(rollbackPath, restoration, CancellationToken.None).ConfigureAwait(false);
            await RequireGuardedHashAsync(restoration, originalSha, CancellationToken.None).ConfigureAwait(false);
            ValidateGuardedTarget(targetPath);
            await RequireGuardedHashAsync(targetPath, proposedSha, CancellationToken.None).ConfigureAwait(false);
            File.Replace(restoration, targetPath, displaced, ignoreMetadataErrors: true);
            var displacedSha = await GuardedHashAsync(displaced, CancellationToken.None).ConfigureAwait(false);
            if (!HashEquals(displacedSha, proposedSha))
            {
                var externalResult = await ReturnConcurrentSaveAsync(targetPath, displaced, displacedSha, originalSha).ConfigureAwait(false);
                return "Während der Wiederherstellung wurde ein weiterer externer Save erkannt. " + externalResult + " Erfasste externe Datei: " + displaced;
            }
            await RequireGuardedHashAsync(targetPath, originalSha, CancellationToken.None).ConfigureAwait(false);
            recovered = true;
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ScsSaveEditException or InvalidDataException or NotSupportedException)
        {
            return "Automatische Wiederherstellung ausgesetzt; die Ziel- oder Wiederherstellungsdatei ist verändert, gesperrt oder nicht prüfbar. " +
                exception.Message + (File.Exists(displaced) ? " Erfasste Datei: " + displaced : string.Empty);
        }
        finally
        {
            TryDelete(restoration);
            if (recovered) TryDelete(displaced);
        }
    }

    private static async Task<string> ReturnConcurrentSaveAsync(string targetPath, string externalPath, string externalSha, string restoredSha)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(targetPath)!, $".nmc-save-external-recovery-{Guid.NewGuid():N}.tmp");
        var captured = Path.Combine(Path.GetDirectoryName(targetPath)!, $".nmc-save-external-captured-{Guid.NewGuid():N}.tmp");
        try
        {
            ValidateGuardedTarget(targetPath);
            await RequireGuardedHashAsync(targetPath, restoredSha, CancellationToken.None).ConfigureAwait(false);
            await CopyGuardedFileAsync(externalPath, temporary, CancellationToken.None).ConfigureAwait(false);
            await RequireGuardedHashAsync(temporary, externalSha, CancellationToken.None).ConfigureAwait(false);
            await RequireGuardedHashAsync(targetPath, restoredSha, CancellationToken.None).ConfigureAwait(false);
            File.Replace(temporary, targetPath, captured, ignoreMetadataErrors: true);
            await RequireGuardedHashAsync(captured, restoredSha, CancellationToken.None).ConfigureAwait(false);
            await RequireGuardedHashAsync(targetPath, externalSha, CancellationToken.None).ConfigureAwait(false);
            TryDelete(captured);
            return "Der externe Dateistand wurde wieder eingesetzt.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ScsSaveEditException or InvalidDataException or NotSupportedException)
        {
            return "Der externe Stand muss anhand der erhaltenen Dateien manuell geprüft werden. " + exception.Message +
                (File.Exists(captured) ? " Zusätzlich erfasste Datei: " + captured : string.Empty);
        }
        finally { TryDelete(temporary); }
    }

    private static ScsSaveEditException RecoveryFailure(string operation, string? backup, string rollback, string reason, Exception inner) =>
        new($"SII-Änderung '{operation}' ist fehlgeschlagen. {reason} Ziel nicht weiter verändern. Backup: {backup ?? "nicht erstellt"}; Wiederherstellungsdatei: {rollback}", inner);

    private void EnsureGameStopped()
    {
        if (_processGuard.IsAnyScsGameRunning())
            throw new ScsSaveEditException("Die verifizierte Änderung erfordert vollständig beendetes ETS2 und ATS.");
    }

    private static void ValidateGuardedTarget(string path)
    {
        ScsDefinitionSource.CheckNoReparsePoints(path);
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(path)!); directory is not null; directory = directory.Parent)
            if (ScsProfileSaveLocator.IsSteamCloudRemoteProfileDirectory(GameType.Ets2, directory.FullName) ||
                ScsProfileSaveLocator.IsSteamCloudRemoteProfileDirectory(GameType.Ats, directory.FullName))
                throw new ScsSaveEditException("Saves in Steam-userdata sind schreibgeschützt; eine direkte Transaktion darf diese Sperre nicht umgehen.");
    }

    private static void CheckExistingAncestry(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (File.Exists(current) || Directory.Exists(current)) ScsDefinitionSource.CheckNoReparsePoints(current);
    }

    private static bool HashEquals(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static async Task RequireGuardedHashAsync(string path, string expected, CancellationToken token)
    {
        if (!HashEquals(await GuardedHashAsync(path, token).ConfigureAwait(false), expected))
            throw new ScsSaveEditException("Die Dateiprüfsumme hat sich geändert; die verifizierte Transaktion wird abgebrochen.");
    }
    private static async Task<string> GuardedHashAsync(string path, CancellationToken token)
    {
        ScsDefinitionSource.CheckNoReparsePoints(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        GuardedSize(stream.Length);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false));
    }
    private static async Task<string> ReadGuardedTextAsync(string path, string expectedSha, CancellationToken token)
    {
        ScsDefinitionSource.CheckNoReparsePoints(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        GuardedSize(stream.Length);
        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        if (stream.ReadByte() != -1 || !HashEquals(Convert.ToHexString(SHA256.HashData(bytes)), expectedSha))
            throw new ScsSaveEditException("Der tatsächlich geschriebene Save entspricht nicht den vorbereiteten Bytes.");
        var content = StrictUtf8NoBom.GetString(bytes);
        _ = ScsSiiTextDocument.Parse(content);
        return content;
    }
    private static async Task CopyGuardedFileAsync(string sourcePath, string destinationPath, CancellationToken token)
    {
        ScsDefinitionSource.CheckNoReparsePoints(sourcePath);
        ScsDefinitionSource.CheckNoReparsePoints(Path.GetDirectoryName(destinationPath)!);
        await using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        GuardedSize(source.Length);
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, token).ConfigureAwait(false);
        await destination.FlushAsync(token).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
    }
    private static void GuardedSize(long length)
    {
        if (length is <= 0 or > MaximumGuardedBytes) throw new ScsSaveEditException("Die Save-Datei ist leer oder überschreitet das Prüflimit von 128 MiB.");
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
        var isLiveWrite = EnsureWriteAllowed(targetPath, writeMode, expectedLastWriteTimeUtc);
        ValidateExpectedLastWriteTime(
            targetPath,
            isLiveWrite ? expectedLastWriteTimeUtc : null);
        _ = await _codec.ReadAsync(fullBackupPath, cancellationToken);
        ValidateExpectedLastWriteTime(
            targetPath,
            isLiveWrite ? expectedLastWriteTimeUtc : null);
        var liveOriginalSha256 = isLiveWrite
            ? await ComputeSha256Async(targetPath, cancellationToken)
            : null;

        var tempPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".nmc-save-restore-{Guid.NewGuid():N}.tmp");
        var rollbackPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".nmc-save-restore-rollback-{Guid.NewGuid():N}.tmp");

        try
        {
            await CopyFileAsync(fullBackupPath, tempPath, cancellationToken);
            _ = await _codec.ReadAsync(tempPath, cancellationToken);
            ValidateExpectedLastWriteTime(
                targetPath,
                isLiveWrite ? expectedLastWriteTimeUtc : null);
            await ValidateUnchangedSha256Async(
                targetPath,
                liveOriginalSha256,
                cancellationToken);
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

    private bool EnsureWriteAllowed(
        string targetPath,
        ScsSaveWriteMode writeMode,
        DateTimeOffset? expectedLastWriteTimeUtc)
    {
        if (!_processGuard.IsAnyScsGameRunning())
            return false;

        var fileName = Path.GetFileName(targetPath);
        if (writeMode == ScsSaveWriteMode.AllowWhileGameRunning &&
            fileName.Equals("game.sii", StringComparison.OrdinalIgnoreCase))
        {
            var saveDirectoryName = Path.GetFileName(Path.GetDirectoryName(targetPath));
            if (ScsSaveKindClassifier.Classify(saveDirectoryName)
                is not (ScsSaveKind.Manual or ScsSaveKind.QuickSave))
            {
                throw new ScsSaveEditException(
                    "Dieser Save-Typ wird bei laufendem ETS2/ATS nicht verändert. " +
                    "Quicksaves und manuelle Saves können direkt bearbeitet werden.");
            }

            if (expectedLastWriteTimeUtc is null)
            {
                throw new ScsSaveEditException(
                    "Bearbeiten bei laufendem ETS2/ATS erfordert einen aktuellen Save-Dateistand. " +
                    "Bitte den Save im Launcher neu laden.");
            }

            return true;
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

    private static async Task ValidateUnchangedSha256Async(
        string targetPath,
        string? expectedSha256,
        CancellationToken cancellationToken)
    {
        if (expectedSha256 is null)
            return;

        var currentSha256 = await ComputeSha256Async(targetPath, cancellationToken);
        if (!string.Equals(currentSha256, expectedSha256, StringComparison.Ordinal))
        {
            throw new ScsSaveEditException(
                "Die Save-Datei wurde während des Schreibvorgangs verändert. " +
                "Die Änderung wurde nicht angewendet; bitte den Save neu laden.");
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

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 128 * 1024;
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await source.CopyToAsync(destination, bufferSize, cancellationToken);
        await destination.FlushAsync(cancellationToken);
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
