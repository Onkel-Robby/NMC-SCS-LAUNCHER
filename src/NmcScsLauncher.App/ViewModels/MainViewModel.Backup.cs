using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.App.Services;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.ViewModels;

public partial class MainViewModel
{
    private IModsetBackupService? _modsetBackupService;
    private IModsetRestoreService? _modsetRestoreService;
    private IModsetBackupDialogService? _modsetBackupDialogService;

    [ObservableProperty] private bool _isBackupActive;
    [ObservableProperty] private double _backupProgress;
    [ObservableProperty] private string _backupProgressText = "Bereit für Backup oder Wiederherstellung.";

    public void ConfigureBackupServices(
        IModsetBackupService backupService,
        IModsetRestoreService restoreService,
        IModsetBackupDialogService dialogService)
    {
        _modsetBackupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
        _modsetRestoreService = restoreService ?? throw new ArgumentNullException(nameof(restoreService));
        _modsetBackupDialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    }

    [RelayCommand]
    private async Task CreateSelectedModsetBackupAsync()
    {
        if (IsBackupActive)
        {
            StatusText = "Es läuft bereits ein Backup- oder Restore-Vorgang.";
            return;
        }

        var modset = SelectedModset;
        if (modset is null)
        {
            StatusText = "Bitte zuerst ein Modset auswählen.";
            return;
        }

        if (_modsetBackupService is null || _modsetBackupDialogService is null)
        {
            StatusText = "Die Backup-Dienste wurden nicht initialisiert.";
            return;
        }

        var dialogResult = _modsetBackupDialogService.ShowCreate(modset);
        if (dialogResult is null) return;

        IsBackupActive = true;
        BackupProgress = 0;
        BackupProgressText = "Backup wird vorbereitet …";
        StatusText = $"Backup für '{modset.Name}' wird erstellt …";

        var progress = new Progress<ModsetBackupProgress>(UpdateBackupProgress);
        try
        {
            var result = await _modsetBackupService.CreateAsync(
                new ModsetBackupRequest(modset.Id, dialogResult.ArchivePath, dialogResult.Content),
                progress);

            await _logger.WriteAsync(
                "INFO",
                $"Backup created for modset {modset.Id}. Files={result.FilesArchived}; Bytes={result.BytesArchived}; Archive={result.ArchivePath}");
            foreach (var warning in result.Warnings)
            {
                await _logger.WriteAsync("WARN", $"Backup warning: {warning}");
            }

            BackupProgress = 100;
            BackupProgressText = $"Backup fertig: {result.FilesArchived} Datei(en), {FormatBytes(result.BytesArchived)}.";
            StatusText = result.Warnings.Count == 0
                ? $"Backup für '{modset.Name}' wurde gespeichert."
                : $"Backup wurde gespeichert. Hinweis: {string.Join(" ", result.Warnings)}";
        }
        catch (OperationCanceledException)
        {
            BackupProgressText = "Backup wurde abgebrochen.";
            StatusText = "Backup wurde abgebrochen. Eine unvollständige .partial-Datei kann zur Kontrolle erhalten geblieben sein.";
        }
        catch (Exception ex) when (ex is ModsetValidationException or DirectoryNotFoundException)
        {
            BackupProgressText = "Backup nicht erstellt.";
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            BackupProgressText = "Backup fehlgeschlagen.";
            StatusText = "Das Backup konnte nicht vollständig erstellt werden. Details wurden protokolliert.";
            await _logger.WriteAsync("ERROR", $"Backup creation failed for modset {modset.Id}.", ex);
        }
        finally
        {
            IsBackupActive = false;
        }
    }

    [RelayCommand]
    private async Task RestoreSelectedModsetBackupAsync()
    {
        if (IsBackupActive)
        {
            StatusText = "Es läuft bereits ein Backup- oder Restore-Vorgang.";
            return;
        }

        var target = SelectedModset;
        if (target is null)
        {
            StatusText = "Bitte zuerst das Ziel-Modset auswählen.";
            return;
        }

        if (_modsetRestoreService is null || _modsetBackupDialogService is null)
        {
            StatusText = "Die Restore-Dienste wurden nicht initialisiert.";
            return;
        }

        var archivePath = _modsetBackupDialogService.SelectRestoreArchive(target);
        if (archivePath is null) return;

        ModsetRestorePreview preview;
        try
        {
            preview = await _modsetRestoreService.InspectAsync(target.Id, archivePath);
        }
        catch (Exception ex) when (ex is ModsetBackupValidationException or FileNotFoundException)
        {
            BackupProgressText = "Backup konnte nicht geprüft werden.";
            StatusText = ex.Message;
            return;
        }
        catch (Exception ex)
        {
            BackupProgressText = "Backup-Prüfung fehlgeschlagen.";
            StatusText = "Die Backup-Datei konnte nicht sicher geprüft werden. Details wurden protokolliert.";
            await _logger.WriteAsync("ERROR", $"Backup inspection failed for target modset {target.Id}.", ex);
            return;
        }

        if (!_modsetBackupDialogService.ConfirmRestore(target, preview))
        {
            StatusText = "Wiederherstellung wurde nicht gestartet.";
            return;
        }

        IsBackupActive = true;
        BackupProgress = 0;
        BackupProgressText = "Wiederherstellung wird vorbereitet …";
        StatusText = $"Backup wird in '{target.Name}' wiederhergestellt …";
        var progress = new Progress<ModsetBackupProgress>(UpdateBackupProgress);

        try
        {
            var result = await _modsetRestoreService.RestoreAsync(
                new ModsetRestoreRequest(target.Id, preview.ArchivePath, preview.ExistingTargetFiles > 0),
                progress);

            await _logger.WriteAsync(
                "INFO",
                $"Backup restored into modset {target.Id}. Files={result.FilesRestored}; Overwritten={result.FilesOverwritten}; Bytes={result.BytesRestored}; Archive={preview.ArchivePath}");

            await InspectSelectedModsetAsync(target);
            BackupProgress = 100;
            BackupProgressText = $"Restore fertig: {result.FilesRestored} Datei(en), {FormatBytes(result.BytesRestored)}.";
            StatusText = result.FilesOverwritten > 0
                ? $"Backup wurde wiederhergestellt. {result.FilesOverwritten} vorhandene Datei(en) wurden nach Bestätigung ersetzt."
                : "Backup wurde wiederhergestellt. Keine vorhandenen Dateien wurden ersetzt.";
        }
        catch (OperationCanceledException)
        {
            BackupProgressText = "Wiederherstellung wurde abgebrochen.";
            StatusText = "Wiederherstellung wurde abgebrochen. Bereits abgeschlossene Einzeldateien bleiben erhalten.";
        }
        catch (ModsetBackupValidationException ex)
        {
            BackupProgressText = "Wiederherstellung nicht abgeschlossen.";
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            BackupProgressText = "Wiederherstellung fehlgeschlagen.";
            StatusText = "Das Backup konnte nicht vollständig wiederhergestellt werden. Details wurden protokolliert.";
            await _logger.WriteAsync("ERROR", $"Backup restore failed for target modset {target.Id}.", ex);
        }
        finally
        {
            IsBackupActive = false;
        }
    }

    private void UpdateBackupProgress(ModsetBackupProgress value)
    {
        BackupProgress = value.Percentage;
        BackupProgressText = value.Stage switch
        {
            "Scanning" => $"Dateien werden vorbereitet: {value.TotalFiles} Datei(en).",
            "Archiving" => $"Sichere {value.FilesProcessed}/{value.TotalFiles}: {value.CurrentItem ?? "Datei"}",
            "Restoring" => $"Stelle wieder her {value.FilesProcessed}/{value.TotalFiles}: {value.CurrentItem ?? "Datei"}",
            "Completed" => $"Fertig: {value.FilesProcessed} Datei(en), {FormatBytes(value.BytesProcessed)}.",
            _ => value.Stage
        };
    }
}
