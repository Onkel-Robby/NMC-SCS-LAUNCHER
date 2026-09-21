using System.Diagnostics;
using System.Windows;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.Services;

public enum ModsetEditorMode
{
    Create,
    Import,
    Edit
}

public sealed record ModsetEditorData(
    GameType Game,
    string Name,
    string? Description,
    string ModDirectoryPath,
    string? PreferredProfile,
    string? AdditionalLaunchArguments);

public interface IModsetEditorService
{
    ModsetEditorData? Show(ModsetEditorMode mode, ModsetEditorData initialData);
}

public interface IConfirmationService
{
    bool ConfirmRemoveFromLauncher(Modset modset);
    bool ConfirmSaveEdit(string title, string message);
}

public interface IExplorerService
{
    void OpenFolder(string path);
    void RevealPath(string path);
}

public sealed class ModsetEditorService : IModsetEditorService
{
    public ModsetEditorData? Show(ModsetEditorMode mode, ModsetEditorData initialData)
    {
        var window = new ModsetEditorWindow(mode, initialData);
        if (Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true ? window.Result : null;
    }
}

public sealed class ConfirmationService : IConfirmationService
{
    public bool ConfirmRemoveFromLauncher(Modset modset)
    {
        ArgumentNullException.ThrowIfNull(modset);
        var result = MessageBox.Show(
            $"Modset '{modset.Name}' aus dem NMC SCS LAUNCHER entfernen?\n\nDie Dateien im Mod-Ordner werden NICHT gelöscht.",
            "Modset entfernen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }

    public bool ConfirmSaveEdit(string title, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var result = MessageBox.Show(
            message + "\n\nReguläre Save-Änderungen erstellen automatisch ein Backup. Das Spiel muss beendet sein.",
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        return result == MessageBoxResult.Yes;
    }
}

public sealed class ExplorerService : IExplorerService
{
    public void OpenFolder(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Das Verzeichnis wurde nicht gefunden: {path}");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public void RevealPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (Directory.Exists(path))
        {
            OpenFolder(path);
            return;
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Die Datei wurde nicht gefunden.", path);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });
    }
}
