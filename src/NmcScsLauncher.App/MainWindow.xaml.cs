using System.Windows;
using System.Windows.Controls;
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
        var dialog = new SaveEditorActionDialog(viewModel, action) { Owner = this };
        dialog.ShowDialog();
    }
}
