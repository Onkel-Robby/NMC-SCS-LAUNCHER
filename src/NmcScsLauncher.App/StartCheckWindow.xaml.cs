using System.Windows;
using NmcScsLauncher.Core;

namespace NmcScsLauncher.App;

public partial class StartCheckWindow : Window
{
    public bool Approved { get; private set; }

    public StartCheckWindow(GameLaunchPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        InitializeComponent();
        DataContext = plan;

        var definition = GameDefinition.For(plan.Modset.Game);
        HeadingTextBlock.Text = $"{definition.DisplayName} – Startprüfung";
        HomePathTextBlock.Text = plan.Modset.HomeBasePath;
        StartButton.IsEnabled = plan.CanLaunch;
        StartButton.Content = plan.HasWarnings ? "Trotzdem starten" : "Spiel starten";
        ResultTextBlock.Text = plan.CanLaunch
            ? plan.HasWarnings
                ? "Es gibt Warnungen. Prüfe sie vor dem Start."
                : "Alle kritischen Prüfungen sind erfolgreich."
            : "Der Start ist wegen mindestens eines kritischen Fehlers gesperrt.";
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        Approved = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
