using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NmcScsLauncher.App.ViewModels;

namespace NmcScsLauncher.App;

/// <summary>
/// A modal presentation for a known Save-Editor action. Actual writes are delegated
/// exclusively to existing, already validated ViewModel commands.
/// </summary>
public partial class SaveEditorActionDialog : Window
{
    public static IReadOnlySet<string> SupportedActionIds { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "money", "experience", "skills", "truck-switch", "truck-repair",
            "fuel", "mileage", "trailer", "cargo", "plates", "backup"
        };

    public SaveEditorActionDialog(MainViewModel viewModel, SaveEditorOverviewAction action)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(action);
        if (!SupportedActionIds.Contains(action.ActionId))
            throw new ArgumentOutOfRangeException(nameof(action), "Unbekannte Save-Editor-Funktion.");

        InitializeComponent();
        DataContext = viewModel;
        Title = $"{action.Title} – NMC SCS LAUNCHER";
        DialogTitle.Text = action.Title;
        DialogDescription.Text = action.Description;

        // Images are purely decorative. The dialog remains usable if a resource cannot be decoded.
        try
        {
            var atlas = new BitmapImage(new Uri(
                "/NmcScsLauncher.App;component/Assets/SaveEditorPreviews.png", UriKind.Relative));
            PreviewBanner.Background = new ImageBrush(atlas)
            {
                Viewbox = action.PreviewViewbox,
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                Stretch = Stretch.Fill
            };
        }
        catch (IOException)
        {
            // Keep the original solid-color banner.
        }
        catch (NotSupportedException)
        {
            // An unsupported image never affects the editor's safety or commands.
        }

        FrameworkElement panel = action.ActionId switch
        {
            "money" => MoneyPanel,
            "experience" => ExperiencePanel,
            "skills" => SkillsPanel,
            "truck-switch" => TruckSwitchPanel,
            "truck-repair" => TruckRepairPanel,
            "fuel" => FuelPanel,
            "mileage" => MileagePanel,
            "trailer" => TrailerPanel,
            "cargo" => CargoPanel,
            "plates" => PlatesPanel,
            "backup" => BackupPanel,
            _ => throw new InvalidOperationException("Unerreichbare Dialogfunktion.")
        };
        panel.Visibility = Visibility.Visible;
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
