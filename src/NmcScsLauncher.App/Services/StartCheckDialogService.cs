using System.Windows;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App.Services;

public interface IStartCheckDialogService
{
    bool ConfirmLaunch(GameLaunchPlan plan);
}

public sealed class StartCheckDialogService : IStartCheckDialogService
{
    public bool ConfirmLaunch(GameLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var window = new StartCheckWindow(plan);
        if (Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            window.Owner = owner;
        }

        return window.ShowDialog() == true && window.Approved;
    }
}
