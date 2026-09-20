using System.Text;
using System.Text.RegularExpressions;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.Infrastructure;

public sealed class ScsSaveEditService : IScsSaveEditService
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly IScsSaveBackupService _backupService;
    private readonly IScsSaveTextCodec _codec;
    private readonly IScsGameProcessGuard _processGuard;

    public ScsSaveEditService(
        IScsSaveBackupService backupService,
        IScsSaveTextCodec codec,
        IScsGameProcessGuard processGuard)
    {
        _backupService = backupService;
        _codec = codec;
        _processGuard = processGuard;
    }

    public Task<ScsSaveEditResult> SetMoneyAsync(
        ScsSaveReference save,
        long amount,
        CancellationToken cancellationToken = default)
    {
        if (amount < 0)
        {
            return Task.FromResult(new ScsSaveEditResult(
                ScsSaveEditStatus.InvalidSelection,
                "Der Geldbetrag darf nicht negativ sein."));
        }

        return ReplaceScalarAsync(save, "money_account", amount, cancellationToken);
    }

    public Task<ScsSaveEditResult> SetExperienceAsync(
        ScsSaveReference save,
        long experiencePoints,
        CancellationToken cancellationToken = default)
    {
        if (experiencePoints < 0)
        {
            return Task.FromResult(new ScsSaveEditResult(
                ScsSaveEditStatus.InvalidSelection,
                "Erfahrungspunkte dürfen nicht negativ sein."));
        }

        return ReplaceScalarAsync(save, "experience_points", experiencePoints, cancellationToken);
    }

    private async Task<ScsSaveEditResult> ReplaceScalarAsync(
        ScsSaveReference save,
        string propertyName,
        long value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(save);

        if (_processGuard.IsGameRunning(save.GameType))
        {
            return new ScsSaveEditResult(
                ScsSaveEditStatus.GameRunning,
                $"{GameDefinition.For(save.GameType).DisplayName} läuft noch. Save-Änderungen sind nur bei beendetem Spiel erlaubt.");
        }

        string targetPath;
        try
        {
            targetPath = ValidateSelection(save);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return new ScsSaveEditResult(ScsSaveEditStatus.InvalidSelection, ex.Message);
        }

        string original;
        try
        {
            original = await _codec.ReadAsPlainTextAsync(targetPath, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            return new ScsSaveEditResult(ScsSaveEditStatus.UnsupportedFormat, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ScsSaveEditResult(
                ScsSaveEditStatus.IoError,
                $"Der Save konnte nicht gelesen werden: {ex.Message}");
        }

        var pattern = $@"(?m)^(?<indent>[ \t]*){Regex.Escape(propertyName)}:[ \t]*-?\d+[ \t]*$";
        var matches = Regex.Matches(original, pattern, RegexOptions.CultureInvariant);

        if (matches.Count == 0)
        {
            return new ScsSaveEditResult(
                ScsSaveEditStatus.PropertyNotFound,
                $"Der Save enthält kein eindeutig bearbeitbares Feld '{propertyName}'.");
        }

        if (matches.Count != 1)
        {
            return new ScsSaveEditResult(
                ScsSaveEditStatus.AmbiguousProperty,
                $"Der Save enthält das Feld '{propertyName}' mehrfach. Aus Sicherheitsgründen wurde nichts geändert.");
        }

        var match = matches[0];
        var replacement = $"{match.Groups["indent"].Value}{propertyName}: {value}";
        var mutated = original.Remove(match.Index, match.Length).Insert(match.Index, replacement);
        if (string.Equals(mutated, original, StringComparison.Ordinal))
        {
            return new ScsSaveEditResult(
                ScsSaveEditStatus.NoChange,
                "Der gewünschte Wert ist bereits gesetzt.");
        }

        ScsSaveBackupResult backup;
        try
        {
            backup = await _backupService.CreateAsync(save, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ScsSaveEditResult(
                ScsSaveEditStatus.IoError,
                $"Vor der Änderung konnte kein vollständiges Save-Backup erstellt werden: {ex.Message}");
        }

        var tempPath = Path.Combine(
            save.SaveDirectory,
            $".{Path.GetFileName(targetPath)}.nmc-edit-{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempPath, mutated, Utf8WithoutBom, cancellationToken);

            var verification = await File.ReadAllTextAsync(tempPath, Utf8WithoutBom, cancellationToken);
            if (!string.Equals(verification, mutated, StringComparison.Ordinal))
                throw new IOException("Die temporär geschriebene Save-Datei konnte nicht verifiziert werden.");

            File.Move(tempPath, targetPath, overwrite: true);

            return new ScsSaveEditResult(
                ScsSaveEditStatus.Success,
                $"'{propertyName}' wurde geändert. Vorher wurde ein vollständiges Save-Backup erstellt.",
                backup.BackupDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteFile(tempPath);
            return new ScsSaveEditResult(
                ScsSaveEditStatus.IoError,
                $"Die Save-Datei konnte nicht sicher ersetzt werden: {ex.Message}",
                backup.BackupDirectory);
        }
    }

    private static string ValidateSelection(ScsSaveReference save)
    {
        var saveDirectory = Path.GetFullPath(save.SaveDirectory);
        var targetPath = Path.GetFullPath(save.GameSiiPath);
        var prefix = saveDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;

        if (!targetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Die ausgewählte game.sii liegt außerhalb des Save-Verzeichnisses.");

        if (!string.Equals(Path.GetFileName(targetPath), "game.sii", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Es dürfen ausschließlich ausgewählte game.sii-Dateien bearbeitet werden.");

        if (!File.Exists(targetPath))
            throw new IOException($"Die ausgewählte game.sii existiert nicht: {targetPath}");

        return targetPath;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup failure must not mask the edit result.
        }
    }
}
