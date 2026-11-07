using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NmcScsLauncher.App.ViewModels;
using NmcScsLauncher.App.Views;
using NmcScsLauncher.Core;
using Xunit;

namespace NmcScsLauncher.App.Tests;

public sealed class LicensePlateBindingTests
{
    [Fact]
    public Task CountrySelectionAndManualCodeAreBoundBothWays() => OnStaThread(() =>
    {
        var model = new LicensePlateEditorViewModel(GameType.Ets2);
        model.Load("\"YN16 MSO|uk\"");
        var control = new LicensePlateEditor { DataContext = model };
        DrainBindings();
        var regions = Element<ComboBox>(control, "RegionComboBox");
        var plate = Element<TextBox>(control, "PlateTextBox");
        var otherPanel = Element<StackPanel>(control, "OtherCountryPanel");
        var otherCode = Element<TextBox>(control, "OtherCountryTextBox");

        Assert.Same(model.SelectedRegion, regions.SelectedItem);
        Assert.Equal("YN16 MSO", plate.Text);
        Assert.Equal(Visibility.Collapsed, otherPanel.Visibility);

        regions.SetCurrentValue(ComboBox.SelectedItemProperty,
            model.Regions.Single(region => region.Code == ScsPlateRegionCatalog.OtherCode));
        otherCode.SetCurrentValue(TextBox.TextProperty, "  promods_region  ");
        plate.SetCurrentValue(TextBox.TextProperty, "NEW 456");
        DrainBindings();

        Assert.True(model.UsesOtherCountry);
        Assert.Equal(Visibility.Visible, otherPanel.Visibility);
        Assert.Equal("promods_region", model.CountryCode);
        Assert.Equal("NEW 456", model.Text);

        model.SelectedRegion = model.Regions.Single(region => region.Code == "germany");
        DrainBindings();
        Assert.Same(model.SelectedRegion, regions.SelectedItem);
        Assert.Equal(Visibility.Collapsed, otherPanel.Visibility);
    });

    [Fact]
    public Task CustomSelectionEnablesColorInputsAndTransfersEdits() => OnStaThread(() =>
    {
        var model = new LicensePlateEditorViewModel(GameType.Ats);
        model.Load("\"ABC 123|texas\"");
        var control = new LicensePlateEditor { DataContext = model };
        DrainBindings();
        var colors = Element<Grid>(control, "ColorsPanel");
        var background = Element<TextBox>(control, "BackgroundRgbTextBox");
        var text = Element<TextBox>(control, "TextRgbTextBox");
        Assert.False(colors.IsEnabled);

        model.SelectedRegion = model.Regions.Single(region => region.Code == "custom");
        DrainBindings();
        Assert.True(colors.IsEnabled);
        background.SetCurrentValue(TextBox.TextProperty, "123456");
        text.SetCurrentValue(TextBox.TextProperty, "abcdef");
        DrainBindings();
        Assert.Equal("123456", model.BackgroundRgb);
        Assert.Equal("abcdef", model.TextRgb);

        model.SelectedRegion = model.Regions.Single(region => region.Code == "california");
        DrainBindings();
        Assert.False(colors.IsEnabled);
    });

    [Fact]
    public Task SeparateControlsKeepVehiclesIndependentAndReflectGameReset() => OnStaThread(() =>
    {
        var truck = new LicensePlateEditorViewModel(GameType.Ets2);
        var trailer = new LicensePlateEditorViewModel(GameType.Ets2);
        truck.Load("\"TRUCK 1|uk\"");
        trailer.Load("\"TRAILER 2|promods_region\"");
        var truckControl = new LicensePlateEditor { DataContext = truck };
        var trailerControl = new LicensePlateEditor { DataContext = trailer };
        DrainBindings();

        Element<TextBox>(truckControl, "PlateTextBox").SetCurrentValue(TextBox.TextProperty, "CHANGED 3");
        DrainBindings();
        Assert.Equal("CHANGED 3", truck.Text);
        Assert.Equal("TRAILER 2", Element<TextBox>(trailerControl, "PlateTextBox").Text);
        Assert.Equal("promods_region", trailer.CountryCode);

        truck.Reset(GameType.Ats);
        DrainBindings();
        var regions = Element<ComboBox>(truckControl, "RegionComboBox");
        Assert.Null(regions.SelectedItem);
        Assert.Empty(Element<TextBox>(truckControl, "PlateTextBox").Text);
        Assert.Contains(regions.Items.Cast<ScsPlateRegion>(), region => region.Code == "texas");
        Assert.DoesNotContain(regions.Items.Cast<ScsPlateRegion>(), region => region.Code == "germany");
        Assert.Equal("promods_region", trailer.CountryCode);
    });

    [Fact]
    public Task UnreadablePlateWarningAndOriginalValueFollowLoadAndClear() => OnStaThread(() =>
    {
        var model = new LicensePlateEditorViewModel(GameType.Ets2);
        var control = new LicensePlateEditor { DataContext = model };
        DrainBindings();
        var warning = Element<TextBlock>(control, "LoadWarningText");
        var savedPanel = Element<StackPanel>(control, "SavedValuePanel");
        var savedText = Element<TextBox>(control, "SavedValueTextBox");
        Assert.Equal(Visibility.Collapsed, warning.Visibility);
        Assert.Equal(Visibility.Collapsed, savedPanel.Visibility);

        model.Load("\"<unknown>123</unknown>|germany\"");
        DrainBindings();
        Assert.Equal(Visibility.Visible, warning.Visibility);
        Assert.Equal(model.LoadWarning, warning.Text);
        Assert.NotEmpty(warning.Text);
        Assert.Equal(Visibility.Visible, savedPanel.Visibility);
        Assert.True(savedText.IsReadOnly);
        Assert.Equal(TextWrapping.Wrap, savedText.TextWrapping);
        Assert.Equal("<unknown>123</unknown>|germany", savedText.Text);

        model.Reset(GameType.Ats);
        DrainBindings();
        Assert.Equal(Visibility.Collapsed, savedPanel.Visibility);
        Assert.Empty(savedText.Text);

        model.Load("\"ABC 123|texas\"");
        DrainBindings();
        Assert.Equal(Visibility.Collapsed, savedPanel.Visibility);

        model.Load("\"<unknown>456</unknown>|texas\"");
        DrainBindings();
        Assert.Equal(Visibility.Visible, savedPanel.Visibility);
        Assert.Equal("<unknown>456</unknown>|texas", savedText.Text);

        model.Load(null);
        DrainBindings();
        Assert.Equal(Visibility.Collapsed, warning.Visibility);
        Assert.Equal(Visibility.Collapsed, savedPanel.Visibility);
        Assert.Empty(savedText.Text);
    });

    private static T Element<T>(FrameworkElement control, string name) where T : FrameworkElement =>
        Assert.IsType<T>(control.FindName(name));

    private static void DrainBindings() =>
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.DataBind, new Action(() => { }));

    private static Task OnStaThread(Action test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                test();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
