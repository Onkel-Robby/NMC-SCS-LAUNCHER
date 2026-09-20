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

        switch (mode)
        {
            case ModsetEditorMode.Create:
                HeadingTextBlock.Text = "Neues Modset";
                SubtitleTextBlock.Text = "Wähle genau den Ordner aus, in dem die Mods dieses Modsets liegen sollen.";
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

        if (Directory.Exists(ModPathTextBox.Text))
        {
            dialog.InitialDirectory = ModPathTextBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            ModPathTextBox.Text = dialog.FolderName;
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

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
