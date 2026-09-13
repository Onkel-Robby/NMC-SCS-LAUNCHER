using System.Windows;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.Services;

public sealed record ModsetDuplicationDialogResult(
    string Name,
    string TargetHomeBasePath,
    ModsetCopyContent Content);

public interface IModsetDuplicationDialogService
{
    ModsetDuplicationDialogResult? Show(Modset source, string suggestedTargetHome);
}

public sealed class ModsetDuplicationDialogService : IModsetDuplicationDialogService
{
    private readonly IFolderPicker _folderPicker;

    public ModsetDuplicationDialogService(IFolderPicker folderPicker)
    {
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
    }

    public ModsetDuplicationDialogResult? Show(Modset source, string suggestedTargetHome)
    {
        ArgumentNullException.ThrowIfNull(source);
        var window = new ModsetDuplicationWindow(source, suggestedTargetHome, _folderPicker);
        if (Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true ? window.Result : null;
    }
}
