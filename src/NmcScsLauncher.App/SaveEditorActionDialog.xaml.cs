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
        SourceInitialized += (_, _) => WindowsTitleBarTheme.Apply(this);
        // The viewport may be shorter than the designed dialog. Keep the fixed
        // footer visible and let the middle editor fields scroll independently.
        Height = Math.Min(
            action.ActionId is "skills" or "plates" ? 710d : 585d,
            Math.Max(MinHeight, SystemParameters.WorkArea.Height - 56d));
        DataContext = viewModel;
        Title = $"{action.Title} – NMC SCS LAUNCHER";
        DialogTitle.Text = action.Title;
        DialogDescription.Text = action.Description;

        // Pack URI is absolute: a relative BitmapImage Uri can be resolved as a
        // filesystem path during deferred WPF rendering, terminating the process.
        // Decode eagerly so even a missing/corrupt decorative image fails here
        // rather than asynchronously in the composition/render dispatcher.
        try
        {
            var atlas = new BitmapImage();
            atlas.BeginInit();
            atlas.UriSource = new Uri(
                "pack://application:,,,/NmcScsLauncher.App;component/Assets/SaveEditorPreviews.png",
                UriKind.Absolute);
            atlas.CacheOption = BitmapCacheOption.OnLoad;
            atlas.EndInit();
            atlas.Freeze();
            PreviewBanner.Background = new ImageBrush(atlas)
            {
                Viewbox = action.PreviewViewbox,
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                Stretch = Stretch.Fill
            };
        }
        catch (IOException)
        {
            // Decorative art is optional: keep the solid-color banner.
        }
        catch (NotSupportedException)
        {
            // Malformed or unsupported images never affect save commands.
        }
        catch (System.Windows.Markup.XamlParseException)
        {
            // A broken pack-resource lookup must not terminate the editor.
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
        SkillsApplyFooterButton.Visibility =
            action.ActionId == "skills" ? Visibility.Visible : Visibility.Collapsed;
        EditorFieldsScrollViewer.ScrollToTop();
    }

    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
