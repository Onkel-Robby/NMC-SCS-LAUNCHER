using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Effects;
using NmcScsLauncher.App.ViewModels;

namespace NmcScsLauncher.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OpenSaveEditorActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SaveEditorOverviewAction action } ||
            DataContext is not MainViewModel viewModel)
            return;

        // The modal freezes the parent selection while editing. All mutations still
        // use the original commands with their unchanged confirmation/backup checks.
        try
        {
            var dialog = new SaveEditorActionDialog(viewModel, action) { Owner = this };
            if (Content is not UIElement root)
            {
                dialog.ShowDialog();
                return;
            }

            // Always restore the main view, even if the dialog raises an exception.
            var priorEffect = root.Effect;
            try
            {
                root.Effect = new BlurEffect { Radius = 5 };
                dialog.ShowDialog();
            }
            finally
            {
                root.Effect = priorEffect;
            }
        }
        catch (Exception exception)
        {
            // UI-boundary fallback: do not terminate the launcher because a modal
            // view fails. Do not execute any edit command or alter a save here.
            Trace.TraceError("NMC Save Editor modal failed for {0}: {1}", action.ActionId, exception);
            viewModel.OpenSaveEditorSectionCommand.Execute(action.Section);
            MessageBox.Show(this,
                "Das Bearbeitungsfenster konnte nicht geöffnet werden. " +
                "Der bisherige Save-Editor wurde stattdessen geöffnet. " +
                "Es wurde kein Spielstand durch diesen Fehlerdialog verändert.",
                "NMC SCS LAUNCHER – Darstellungsfehler",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
