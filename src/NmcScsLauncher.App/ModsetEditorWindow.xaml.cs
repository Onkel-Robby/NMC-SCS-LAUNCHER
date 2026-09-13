using System.IO;
using System.Windows;
using Microsoft.Win32;
using NmcScsLauncher.App.Services;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App;

public partial class ModsetEditorWindow : Window
{
    private readonly ModsetEditorMode _mode;

    public ModsetEditorData? Result { get; private set; }

    public ModsetEditorWindow(ModsetEditorMode mode, ModsetEditorData initialData)
    {
        InitializeComponent();
        _mode = mode;

        GameComboBox.ItemsSource = Enum.GetValues<GameType>();
        GameComboBox.SelectedItem = initialData.Game;
        NameTextBox.Text = initialData.Name;
        DescriptionTextBox.Text = initialData.Description ?? string.Empty;
        HomePathTextBox.Text = initialData.HomeBasePath;
        PreferredProfileTextBox.Text = initialData.PreferredProfile ?? string.Empty;
        LaunchArgumentsTextBox.Text = initialData.AdditionalLaunchArguments ?? string.Empty;

        switch (mode)
        {
            case ModsetEditorMode.Create:
                HeadingTextBlock.Text = "Neues Modset";
                SubtitleTextBlock.Text = "Es wird ein separates Home-Verzeichnis für dieses Modset angelegt.";
                SaveButton.Content = "Modset erstellen";
                break;
            case ModsetEditorMode.Import:
                HeadingTextBlock.Text = "Bestehendes Modset importieren";
                SubtitleTextBlock.Text = "Der vorhandene Ordner wird nur registriert. Dateien werden nicht verändert.";
                SaveButton.Content = "Importieren";
                break;
            case ModsetEditorMode.Edit:
                HeadingTextBlock.Text = "Modset bearbeiten";
                SubtitleTextBlock.Text = "Änderungen am Pfad verschieben oder löschen keine vorhandenen Dateien.";
                SaveButton.Content = "Änderungen speichern";
                break;
        }
    }

    private void BrowseHome_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = _mode == ModsetEditorMode.Import
                ? "Bestehendes SCS-Home-Verzeichnis auswählen"
                : "Home-Verzeichnis auswählen",
            Multiselect = false
        };

        if (Directory.Exists(HomePathTextBox.Text))
        {
            dialog.InitialDirectory = HomePathTextBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            HomePathTextBox.Text = dialog.FolderName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ValidationTextBlock.Text = string.Empty;
        var name = NameTextBox.Text.Trim();
        var homePath = HomePathTextBox.Text.Trim();

        if (name.Length == 0)
        {
            ValidationTextBlock.Text = "Bitte einen Namen eingeben.";
            return;
        }

        if (homePath.Length == 0)
        {
            ValidationTextBlock.Text = "Bitte ein Home-Verzeichnis angeben.";
            return;
        }

        if ((_mode is ModsetEditorMode.Import or ModsetEditorMode.Edit) && !Directory.Exists(homePath))
        {
            ValidationTextBlock.Text = "Das angegebene Verzeichnis existiert nicht.";
            return;
        }

        Result = new ModsetEditorData(
            (GameType)(GameComboBox.SelectedItem ?? GameType.Ets2),
            name,
            NullIfWhiteSpace(DescriptionTextBox.Text),
            homePath,
            NullIfWhiteSpace(PreferredProfileTextBox.Text),
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
