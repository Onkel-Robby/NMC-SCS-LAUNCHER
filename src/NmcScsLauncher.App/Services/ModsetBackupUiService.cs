using System.Windows;
using Microsoft.Win32;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.Services;

public sealed record ModsetBackupDialogResult(
    string ArchivePath,
    ModsetBackupContent Content);

public interface IModsetBackupDialogService
{
    ModsetBackupDialogResult? ShowCreate(Modset source);

    string? SelectRestoreArchive(Modset target);

    bool ConfirmRestore(Modset target, ModsetRestorePreview preview);
}

public sealed class ModsetBackupDialogService : IModsetBackupDialogService
{
    public ModsetBackupDialogResult? ShowCreate(Modset source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var optionsWindow = new ModsetBackupWindow(source);
        if (Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            optionsWindow.Owner = owner;
        }

        if (optionsWindow.ShowDialog() != true || optionsWindow.ContentSelection == ModsetBackupContent.None)
        {
            return null;
        }

        var dialog = new SaveFileDialog
        {
            Title = $"Backup für {source.Name} speichern",
            Filter = "ZIP-Backup (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = BuildSuggestedFileName(source)
        };

        var result = Application.Current?.MainWindow is { IsVisible: true } dialogOwner
            ? dialog.ShowDialog(dialogOwner)
            : dialog.ShowDialog();

        return result == true
            ? new ModsetBackupDialogResult(dialog.FileName, optionsWindow.ContentSelection)
            : null;
    }

    public string? SelectRestoreArchive(Modset target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var dialog = new OpenFileDialog
        {
            Title = $"Backup für {target.Name} auswählen",
            Filter = "NMC SCS Launcher Backups (*.zip)|*.zip|Alle Dateien (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        var result = Application.Current?.MainWindow is { IsVisible: true } owner
            ? dialog.ShowDialog(owner)
            : dialog.ShowDialog();
        return result == true ? dialog.FileName : null;
    }

    public bool ConfirmRestore(Modset target, ModsetRestorePreview preview)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(preview);

        var overwriteText = preview.ExistingTargetFiles > 0
            ? $"\nACHTUNG: {preview.ExistingTargetFiles} vorhandene Datei(en) werden ersetzt."
            : "\nEs werden keine vorhandenen Dateien ersetzt.";

        var result = MessageBox.Show(
            $"Backup in Modset '{target.Name}' wiederherstellen?\n\n" +
            $"Backup von: {preview.Manifest.ModsetName}\n" +
            $"Spiel: {GameDefinition.For(preview.Manifest.Game).DisplayName}\n" +
            $"Erstellt: {preview.Manifest.CreatedAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}\n" +
            $"Dateien: {preview.FilesInArchive}\n" +
            $"Neu: {preview.NewTargetFiles}\n" +
            $"Vorhanden: {preview.ExistingTargetFiles}" +
            overwriteText +
            "\n\nNicht im Backup enthaltene Dateien bleiben unverändert.",
            "Backup wiederherstellen",
            MessageBoxButton.YesNo,
            preview.ExistingTargetFiles > 0 ? MessageBoxImage.Warning : MessageBoxImage.Question,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }

    private static string BuildSuggestedFileName(Modset source)
    {
        var game = source.Game == GameType.Ets2 ? "ETS2" : "ATS";
        var invalid = Path.GetInvalidFileNameChars();
        var safeName = new string(source.Name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (safeName.Length == 0) safeName = "Modset";
        return $"{DateTime.Now:yyyy-MM-dd_HHmm}_{game}_{safeName}.zip";
    }
}
