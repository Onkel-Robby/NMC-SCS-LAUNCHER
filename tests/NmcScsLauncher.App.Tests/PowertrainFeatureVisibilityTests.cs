using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using NmcScsLauncher.App.Views;
using Xunit;

namespace NmcScsLauncher.App.Tests;

public sealed class PowertrainFeatureVisibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StandalonePowertrainViewsAreHiddenAndCannotReceiveKeyboardInput(bool catalog) => OnStaThread(() =>
    {
        var view = CreateView(catalog);
        foreach (var context in new object?[] { null, new object(), new SaveEditorTestHarness(powertrainEnabled: false).ViewModel })
        {
            view.DataContext = context;
            Layout(view);
            Assert.Equal(Visibility.Collapsed, view.Visibility);
            Assert.False(view.IsEnabled);
            Assert.Equal(KeyboardNavigationMode.None, KeyboardNavigation.GetTabNavigation(view));
            Assert.Equal(new Size(0, 0), view.DesiredSize);
            AssertHiddenControlsCannotReceiveFocus(view);
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ControlledReactivationRestoresViewsAndContextRemovalHidesThemAgain(bool catalog) => OnStaThread(() =>
    {
        var view = CreateView(catalog);
        view.DataContext = new SaveEditorTestHarness(powertrainEnabled: true).ViewModel;
        Layout(view);
        Assert.Equal(Visibility.Visible, view.Visibility);
        Assert.True(view.IsEnabled);
        Assert.True(view.DesiredSize.Height > 0);
        view.DataContext = null;
        Layout(view);
        Assert.Equal(Visibility.Collapsed, view.Visibility);
        Assert.False(view.IsEnabled);
        Assert.Equal(new Size(0, 0), view.DesiredSize);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RealTruckTabReflowsAndWholeCatalogTabFollowsTheFeatureGate(bool enabled) => OnStaThread(() =>
    {
        var tabs = LoadRealTruckAndCatalogTabs();
        tabs.DataContext = new SaveEditorTestHarness(powertrainEnabled: enabled).ViewModel;
        tabs.SelectedIndex = 0;
        Layout(tabs);
        var truck = Assert.IsType<TabItem>(tabs.Items[0]);
        var catalog = Assert.IsType<TabItem>(tabs.Items[1]);
        var layout = Assert.IsType<Grid>(truck.Content);
        var vehicleFunctions = Assert.IsType<Grid>(layout.Children[0]);
        var powertrainContainer = Assert.IsType<Grid>(layout.Children[1]);

        Assert.Equal(enabled ? Visibility.Visible : Visibility.Collapsed, catalog.Visibility);
        Assert.Equal(enabled, catalog.IsEnabled);
        Assert.Equal(enabled, catalog.IsTabStop);
        Assert.Equal(enabled ? Visibility.Visible : Visibility.Collapsed, powertrainContainer.Visibility);
        Assert.Equal(enabled, powertrainContainer.IsEnabled);
        Assert.Equal(enabled ? 1 : 3, Grid.GetColumnSpan(vehicleFunctions));
        Assert.Equal(Visibility.Visible, vehicleFunctions.Visibility);
        Assert.True(vehicleFunctions.IsEnabled);
        Assert.Contains(Descendants(vehicleFunctions).OfType<Button>(), button => Equals(button.Content, "Truck reparieren"));
        Assert.Contains(Descendants(vehicleFunctions).OfType<Button>(), button => Equals(button.Content, "Wechseln"));
        if (!enabled)
        {
            Assert.True(layout.ActualWidth > 0);
            Assert.Equal(layout.ActualWidth, vehicleFunctions.ActualWidth, precision: 3);
            Assert.Equal(new Size(0, 0), powertrainContainer.DesiredSize);
            AssertHiddenControlsCannotReceiveFocus(powertrainContainer);
            AssertHiddenControlsCannotReceiveFocus(catalog);
        }
    });

    // Load the actual production tab markup without creating App or invoking startup/LicenseHub.
    // Only application-wide colors are supplied locally; layout and feature triggers are unchanged.
    private static TabControl LoadRealTruckAndCatalogTabs()
    {
        using var stream = typeof(PowertrainFeatureVisibilityTests).Assembly
            .GetManifestResourceStream("NmcScsLauncher.Tests.MainWindow.xaml")!;
        var document = XDocument.Load(stream);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var truck = document.Descendants(presentation + "TabItem").Single(item => (string?)item.Attribute("Header") == "Truck");
        var sourceTabs = truck.Parent!;
        var catalog = sourceTabs.Elements(presentation + "TabItem").Single(item => (string?)item.Attribute("Header") == "Komponentenkatalog");
        var resources = new XElement(sourceTabs.Element(presentation + "TabControl.Resources")!);
        foreach (var name in new[] { "SecondaryTextBrush", "CardBrush", "BorderBrush", "AccentBrush", "PrimaryTextBrush", "SuccessBrush" })
            resources.AddFirst(new XElement(presentation + "SolidColorBrush", new XAttribute(x + "Key", name), new XAttribute("Color", "Gray")));
        var root = new XElement(presentation + "TabControl", new XAttribute(XNamespace.Xmlns + "x", x.NamespaceName), resources,
            new XElement(truck), new XElement(catalog));
        var catalogControl = root.Descendants().Single(element => element.Name.LocalName == nameof(PowertrainCatalog));
        catalogControl.ReplaceWith(CatalogMarkup());
        foreach (var element in root.Descendants().Where(element => element.Name.NamespaceName == "clr-namespace:NmcScsLauncher.App.Views"))
            element.Name = XName.Get(element.Name.LocalName, "clr-namespace:NmcScsLauncher.App.Views;assembly=NmcScsLauncher.App");
        return Assert.IsType<TabControl>(XamlReader.Parse(root.ToString()));
    }

    private static UserControl CreateView(bool catalog) => catalog
        ? Assert.IsType<UserControl>(XamlReader.Parse(CatalogMarkup().ToString()))
        : new TruckPowertrainSelection();

    private static XElement CatalogMarkup()
    {
        using var stream = typeof(PowertrainFeatureVisibilityTests).Assembly
            .GetManifestResourceStream("NmcScsLauncher.Tests.PowertrainCatalog.xaml")!;
        var root = XDocument.Load(stream).Root!;
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        root.Attribute(x + "Class")!.Remove();
        // PowertrainCatalog's code-behind only initializes this markup. Supply its one
        // application-level style locally, avoiding an Application singleton across STA tests.
        root.AddFirst(new XElement(presentation + "UserControl.Resources",
            new XElement(presentation + "Style", new XAttribute(x + "Key", "GridPathTextStyle"),
                new XAttribute("TargetType", "TextBlock"))));
        return root;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void AssertHiddenControlsCannotReceiveFocus(DependencyObject root)
    {
        // Collapsed UserControl content need not have an instantiated visual template,
        // so its logical children's IsEnabled is not yet inherited from the disabled root.
        Assert.All(Descendants(root).OfType<Control>(), control =>
        {
            Assert.False(control.IsVisible);
            Assert.False(control.Focus());
            Assert.False(control.IsKeyboardFocusWithin);
        });
    }

    private static void Layout(FrameworkElement view)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        view.Measure(new Size(1000, 1200));
        view.Arrange(new Rect(0, 0, 1000, 1200));
        view.UpdateLayout();
    }

    private static Task OnStaThread(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception exception) { completion.SetException(exception); }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
