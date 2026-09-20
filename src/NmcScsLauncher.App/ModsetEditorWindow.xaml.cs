using System.IO;
using System.Windows;
using Microsoft.Win32;
using NmcScsLauncher.App.Services;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App;

public partial class ModsetEditorWindow : Window
{
    private readonly ModsetEditorMode _mode;
    private readonly string? _preferredProfile;

    public ModsetEditorData? Result { get; private set; }

    public ModsetEditorWindow(ModsetEditorMode mode, ModsetEditorData initialData)
    {
        InitializeComponent();
        _mode = mode;
        _preferredProfile = initialData.PreferredProfile;

        GameComboBox.ItemsSource = Enum.GetValues<GameType>();
        GameComboBox.SelectedItem = initialData.Game;
        NameTextBox.Text = initialData.Name;
        DescriptionTextBox.Text = initialData.Description ?? string.Empty;
        ModPathTextBox.Text = initialData.ModDirectoryPath;
        LaunchArgumentsTextBox.Text = initialData.AdditionalLaunchArguments ?? string.Empty;
        CreateFolderButton.Visibility = mode == ModsetEditorMode.Create ? Visibility.Visible : Visibility.Collapsed;

        switch (mode)
        {
            case ModsetEditorMode.Create:
                HeadingTextBlock.Text = "Neues Modset";
                SubtitleTextBlock.Text = "Wähle einen vorhandenen Mod-Ordner aus oder lege den eingetragenen neuen Ordner direkt an.";
                SaveButton.Content = "Modset erstellen";
                break;
            case ModsetEditorMode.Import:
                HeadingTextBlock.Text = "Bestehendes Modset importieren";
                SubtitleTextBlock.Text = "Der vorhandene Mod-Ordner wird nur registriert. Dateien werden nicht verändert.";
                SaveButton.Content = "Importieren";
                break;
            case ModsetEditorMode.Edit:
                HeadingTextBlock.Text = "Modset bearbeiten";
                SubtitleTextBlock.Text = "Der ausgewählte Pfad ist direkt der Mod-Ordner. Dateien werden nicht verschoben oder gelöscht.";
                SaveButton.Content = "Änderungen speichern";
                break;
        }
    }

    private void BrowseMod_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = _mode == ModsetEditorMode.Import
                ? "Bestehenden Mod-Ordner auswählen"
                : "Mod-Ordner auswählen",
            Multiselect = false
        };

        var initialDirectory = FindExistingDirectory(ModPathTextBox.Text);
        if (initialDirectory is not null)
        {
            dialog.InitialDirectory = initialDirectory;
        }

        if (dialog.ShowDialog(this) == true)
        {
            ModPathTextBox.Text = dialog.FolderName;
        }
    }

    private void CreateFolder_Click(object sender, RoutedEventArgs e)
    {
        ValidationTextBlock.Text = string.Empty;
        var modPath = ModPathTextBox.Text.Trim();

        if (modPath.Length == 0)
        {
            ValidationTextBlock.Text = "Bitte zuerst einen Mod-Ordner-Pfad angeben.";
            return;
        }

        if (!Path.IsPathFullyQualified(modPath))
        {
            ValidationTextBlock.Text = "Bitte einen absoluten Mod-Ordner-Pfad angeben.";
            return;
        }

        try
        {
            Directory.CreateDirectory(modPath);
            ValidationTextBlock.Text = "Mod-Ordner wurde angelegt.";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
                                   or IOException
                                   or ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException)
        {
            ValidationTextBlock.Text = $"Mod-Ordner konnte nicht angelegt werden: {ex.Message}";
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ValidationTextBlock.Text = string.Empty;
        var name = NameTextBox.Text.Trim();
        var modPath = ModPathTextBox.Text.Trim();

        if (name.Length == 0)
        {
            ValidationTextBlock.Text = "Bitte einen Namen eingeben.";
            return;
        }

        if (modPath.Length == 0)
        {
            ValidationTextBlock.Text = "Bitte einen Mod-Ordner angeben.";
            return;
        }

        if (!Path.IsPathFullyQualified(modPath))
        {
            ValidationTextBlock.Text = "Bitte einen absoluten Mod-Ordner-Pfad angeben.";
            return;
        }

        if ((_mode is ModsetEditorMode.Import or ModsetEditorMode.Edit) && !Directory.Exists(modPath))
        {
            ValidationTextBlock.Text = "Der angegebene Mod-Ordner existiert nicht.";
            return;
        }

        Result = new ModsetEditorData(
            (GameType)(GameComboBox.SelectedItem ?? GameType.Ets2),
            name,
            NullIfWhiteSpace(DescriptionTextBox.Text),
            modPath,
            _preferredProfile,
            NullIfWhiteSpace(LaunchArgumentsTextBox.Text));

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static string? FindExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var current = Path.GetFullPath(path.Trim());
            while (!Directory.Exists(current))
            {
                var parent = Directory.GetParent(current);
                if (parent is null)
                {
                    return null;
                }

                current = parent.FullName;
            }

            return current;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
