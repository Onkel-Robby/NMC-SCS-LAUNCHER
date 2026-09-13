using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NmcScsLauncher.App.Services;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.ViewModels;

public partial class MainViewModel
{
    private IModsetDuplicationService? _modsetDuplicationService;
    private IModsetDuplicationDialogService? _modsetDuplicationDialogService;

    [ObservableProperty] private bool _isDuplicationActive;
    [ObservableProperty] private double _duplicationProgress;
    [ObservableProperty] private string _duplicationProgressText = "Bereit zum Duplizieren.";

    public void ConfigureDuplicationServices(
        IModsetDuplicationService duplicationService,
        IModsetDuplicationDialogService dialogService)
    {
        _modsetDuplicationService = duplicationService ?? throw new ArgumentNullException(nameof(duplicationService));
        _modsetDuplicationDialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
    }

    [RelayCommand]
    private async Task DuplicateSelectedModsetAsync()
    {
        if (IsDuplicationActive)
        {
            StatusText = "Es läuft bereits eine Modset-Duplizierung.";
            return;
        }

        var source = SelectedModset;
        if (source is null)
        {
            StatusText = "Bitte zuerst ein Modset auswählen.";
            return;
        }

        if (_modsetDuplicationService is null || _modsetDuplicationDialogService is null)
        {
            StatusText = "Die Duplizierungsdienste wurden nicht initialisiert.";
            return;
        }

        var copyName = BuildAvailableCopyName(source);
        var suggestedTarget = BuildSuggestedHomePath(source.Game, copyName);
        var dialogResult = _modsetDuplicationDialogService.Show(source, suggestedTarget);
        if (dialogResult is null) return;

        IsDuplicationActive = true;
        DuplicationProgress = 0;
        DuplicationProgressText = "Kopierplan wird erstellt …";
        StatusText = $"Modset '{source.Name}' wird dupliziert …";

        var progress = new Progress<ModsetDuplicationProgress>(value =>
        {
            DuplicationProgress = value.Percentage;
            DuplicationProgressText = value.Stage switch
            {
                "Scanning" => $"Dateien werden vorbereitet: {value.TotalFiles} Datei(en).",
                "Copying" => $"Kopiere {value.FilesCopied}/{value.TotalFiles}: {value.CurrentItem ?? "Datei"}",
                "Registering" => "Kopie wird im Launcher registriert …",
                "Completed" => $"Fertig: {value.FilesCopied} Datei(en) kopiert.",
                _ => value.Stage
            };
        });

        try
        {
            var result = await _modsetDuplicationService.DuplicateAsync(
                new ModsetDuplicationRequest(source.Id, dialogResult.Name, dialogResult.TargetHomeBasePath, dialogResult.Content),
                progress);

            await _logger.WriteAsync(
                "INFO",
                $"Duplicated modset {source.Id} to {result.Modset.Id}. Files={result.FilesCopied}; Bytes={result.BytesCopied}; Target={result.Modset.HomeBasePath}");

            foreach (var warning in result.Warnings)
            {
                await _logger.WriteAsync("WARN", $"Modset duplication warning: {warning}");
            }

            await LoadModsetsAsync();
            SelectedModset = Modsets.FirstOrDefault(item => item.Id == result.Modset.Id);
            DuplicationProgress = 100;
            DuplicationProgressText = $"Fertig: {result.FilesCopied} Datei(en), {FormatBytes(result.BytesCopied)}.";
            StatusText = result.Warnings.Count == 0
                ? $"Modset '{source.Name}' wurde als '{result.Modset.Name}' dupliziert."
                : $"Modset wurde dupliziert. Hinweis: {string.Join(" ", result.Warnings)}";
        }
        catch (OperationCanceledException)
        {
            DuplicationProgressText = "Duplizierung wurde abgebrochen.";
            StatusText = "Duplizierung wurde abgebrochen. Ein unvollständiger Staging-Ordner kann zur Kontrolle erhalten geblieben sein.";
        }
        catch (ModsetValidationException ex)
        {
            DuplicationProgressText = "Duplizierung nicht gestartet.";
            StatusText = ex.Message;
        }
        catch (Exception ex)
        {
            DuplicationProgressText = "Duplizierung fehlgeschlagen.";
            StatusText = "Das Modset konnte nicht vollständig dupliziert werden. Details wurden protokolliert.";
            await _logger.WriteAsync("ERROR", $"Modset duplication failed for source {source.Id}.", ex);
        }
        finally
        {
            IsDuplicationActive = false;
        }
    }

    private string BuildAvailableCopyName(Modset source)
    {
        var baseName = source.Name + " Kopie";
        if (!Modsets.Any(item => item.Game == source.Game && string.Equals(item.Name, baseName, StringComparison.CurrentCultureIgnoreCase)))
            return baseName;

        for (var number = 2; number < 1000; number++)
        {
            var candidate = $"{baseName} {number}";
            if (!Modsets.Any(item => item.Game == source.Game && string.Equals(item.Name, candidate, StringComparison.CurrentCultureIgnoreCase)))
                return candidate;
        }

        return $"{baseName} {DateTime.Now:yyyyMMdd-HHmmss}";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024L * 1024) return $"{bytes / 1024d:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024d * 1024):F1} MB";
        return $"{bytes / (1024d * 1024 * 1024):F2} GB";
    }
}
