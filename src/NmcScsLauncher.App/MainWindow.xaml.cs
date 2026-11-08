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
        var dialog = new SaveEditorActionDialog(viewModel, action) { Owner = this };
        if (Content is not UIElement root)
        {
            dialog.ShowDialog();
            return;
        }

        // Temporary visual treatment only; modality keeps profile and save selection fixed.
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
}
