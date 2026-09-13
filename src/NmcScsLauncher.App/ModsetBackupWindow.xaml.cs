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
