using System.IO;
using System.Windows;
using System.Windows.Controls;
using NmcScsLauncher.App.Services;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App;

public partial class ModsetDuplicationWindow : Window
{
    private readonly Modset _source;
    private readonly IFolderPicker _folderPicker;
    private readonly string _initialSuggestedTarget;

    public ModsetDuplicationDialogResult? Result { get; private set; }

    public ModsetDuplicationWindow(Modset source, string suggestedTargetHome, IFolderPicker folderPicker)
    {
        InitializeComponent();
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _initialSuggestedTarget = suggestedTargetHome ?? throw new ArgumentNullException(nameof(suggestedTargetHome));

        SourceTextBlock.Text = $"Quelle: {source.Name} · {GameDefinition.For(source.Game).DisplayName}";
        NameTextBox.Text = source.Name + " Kopie";
        TargetPathTextBox.Text = suggestedTargetHome;

        if (!string.IsNullOrWhiteSpace(source.ModDirectoryPath))
        {
            TargetPathLabel.Text = "Neuer Mod-Ordner";
            TargetPathHint.Text = "Der Zielordner muss neu sein. Er wird direkt als Mod-Ordner der Kopie verwendet.";
            ConfigurationCheckBox.IsChecked = false;
            ConfigurationCheckBox.IsEnabled = false;
            ProfilesCheckBox.IsChecked = false;
            ProfilesCheckBox.IsEnabled = false;
            ScreenshotsCheckBox.IsChecked = false;
            ScreenshotsCheckBox.IsEnabled = false;
            LogsCheckBox.IsChecked = false;
            LogsCheckBox.IsEnabled = false;
            ModsCheckBox.IsChecked = true;
            CopyInfoTextBlock.Text = "Bei direkten Mod-Ordnern werden nur die Mod-Dateien dupliziert. Profile bleiben in den normalen SCS-Pfaden und werden nicht kopiert.";
        }
    }

    private void BrowseTarget_OnClick(object sender, RoutedEventArgs e)
    {
        var initial = TryGetExistingParent(TargetPathTextBox.Text) ?? TryGetExistingParent(_initialSuggestedTarget);
        var parent = _folderPicker.PickFolder("Übergeordneten Zielordner für die Modset-Kopie auswählen", initial);
        if (parent is null) return;

        var folderName = SanitizeFolderName(NameTextBox.Text);
        TargetPathTextBox.Text = Path.Combine(parent, folderName.Length == 0 ? "Modset Kopie" : folderName);
    }

    private void NameTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || string.IsNullOrWhiteSpace(TargetPathTextBox.Text)) return;
        if (!PathsShareSuggestedParent(TargetPathTextBox.Text, _initialSuggestedTarget)) return;

        var parent = Path.GetDirectoryName(TargetPathTextBox.Text);
        if (string.IsNullOrWhiteSpace(parent)) return;
        var folderName = SanitizeFolderName(NameTextBox.Text);
        if (folderName.Length > 0) TargetPathTextBox.Text = Path.Combine(parent, folderName);
    }

    private void Duplicate_OnClick(object sender, RoutedEventArgs e)
    {
        ValidationTextBlock.Text = string.Empty;
        var name = NameTextBox.Text.Trim();
        var target = TargetPathTextBox.Text.Trim();
        if (name.Length == 0)
        {
            ValidationTextBlock.Text = "Bitte einen Namen für die Kopie angeben.";
            return;
        }
        if (target.Length == 0 || !Path.IsPathFullyQualified(target))
        {
            ValidationTextBlock.Text = "Bitte einen absoluten neuen Zielordner angeben.";
            return;
        }
        if (Directory.Exists(target) || File.Exists(target))
        {
            ValidationTextBlock.Text = "Der Zielordner existiert bereits. Bitte einen neuen Ordner verwenden.";
            return;
        }

        var content = ModsetCopyContent.None;
        if (ConfigurationCheckBox.IsChecked == true) content |= ModsetCopyContent.Configuration;
        if (ModsCheckBox.IsChecked == true) content |= ModsetCopyContent.Mods;
        if (ProfilesCheckBox.IsChecked == true) content |= ModsetCopyContent.Profiles;
        if (ScreenshotsCheckBox.IsChecked == true) content |= ModsetCopyContent.Screenshots;
        if (LogsCheckBox.IsChecked == true) content |= ModsetCopyContent.Logs;

        Result = new ModsetDuplicationDialogResult(name, target, content);
        DialogResult = true;
    }

    private static string? TryGetExistingParent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var current = Path.GetFullPath(path);
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (Directory.Exists(current)) return current;
                current = Path.GetDirectoryName(current);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
        return null;
    }

    private static string SanitizeFolderName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim().TrimEnd('.');
    }

    private static bool PathsShareSuggestedParent(string current, string suggested)
    {
        try
        {
            var currentParent = Path.GetDirectoryName(Path.GetFullPath(current));
            var suggestedParent = Path.GetDirectoryName(Path.GetFullPath(suggested));
            return string.Equals(currentParent, suggestedParent, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
