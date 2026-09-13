using System.Windows;
using NmcScsLauncher.App.ViewModels;

namespace NmcScsLauncher.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
