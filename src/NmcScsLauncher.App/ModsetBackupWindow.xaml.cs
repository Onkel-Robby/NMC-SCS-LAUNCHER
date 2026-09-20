using System.Windows;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App;

public partial class ModsetBackupWindow : Window
{
    public ModsetBackupContent ContentSelection { get; private set; } = ModsetBackupContent.None;

    public ModsetBackupWindow(Modset source)
    {
        InitializeComponent();
        ArgumentNullException.ThrowIfNull(source);
        SourceTextBlock.Text = $"Modset: {source.Name} · {GameDefinition.For(source.Game).DisplayName}";

        if (!string.IsNullOrWhiteSpace(source.ModDirectoryPath))
        {
            ConfigurationCheckBox.IsChecked = false;
            ConfigurationCheckBox.IsEnabled = false;
            ProfilesCheckBox.IsChecked = false;
            ProfilesCheckBox.IsEnabled = false;
            ModsCheckBox.IsChecked = true;
            BackupInfoTextBlock.Text = "Bei direkten Mod-Ordnern sichert das Modset-Backup nur die Dateien aus dem ausgewählten Mod-Ordner. Die normalen lokalen und Steam-Profile bleiben außerhalb des Modsets.";
        }
    }

    private void Continue_OnClick(object sender, RoutedEventArgs e)
    {
        var content = ModsetBackupContent.None;
        if (ConfigurationCheckBox.IsChecked == true) content |= ModsetBackupContent.Configuration;
        if (ProfilesCheckBox.IsChecked == true) content |= ModsetBackupContent.Profiles;
        if (ModsCheckBox.IsChecked == true) content |= ModsetBackupContent.Mods;

        if (content == ModsetBackupContent.None)
        {
            ValidationTextBlock.Text = "Bitte mindestens einen Backup-Inhalt auswählen.";
            return;
        }

        ContentSelection = content;
        DialogResult = true;
    }
}
