using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NmcScsLauncher.App.ViewModels;
using Xunit;

namespace NmcScsLauncher.App.Tests;

/// <summary>Exercise the compiled WPF dialog and force pack-image decoding.
/// Does not show windows, start LicenseHub or open any actual saves.</summary>
public sealed class SaveEditorActionDialogRuntimeTests
{
    [Fact]
    public Task AllElevenDialogsConstructAndDecodeTheirPackedImages() => OnStaThread(() =>
    {
        if (Application.Current is null)
        {
            var app = new NmcScsLauncher.App.App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        var viewModel = new SaveEditorTestHarness().ViewModel;
        foreach (var action in viewModel.SaveEditorOverviewActions)
        {
            var dialog = new SaveEditorActionDialog(viewModel, action);
            Assert.Equal(action.Title + " – NMC SCS LAUNCHER", dialog.Title);
            var preview = Assert.IsType<Border>(dialog.FindName("PreviewBanner"));
            var brush = Assert.IsType<ImageBrush>(preview.Background);
            var image = Assert.IsType<BitmapImage>(brush.ImageSource);
            Assert.Equal("pack", image.UriSource.Scheme);
            Assert.True(image.PixelWidth > 0, action.ActionId);
            Assert.True(image.IsFrozen);
            Assert.Equal(action.PreviewViewbox, brush.Viewbox);

            // Check the actual compiled styles, not only the XAML strings:
            // an explicitly styled TextBox must not revert to white controls.
            var moneyPanel = Assert.IsType<StackPanel>(dialog.FindName("MoneyPanel"));
            var moneyField = Assert.Single(moneyPanel.Children.OfType<TextBox>());
            var fieldColor = Assert.IsType<SolidColorBrush>(moneyField.Background).Color;
            Assert.Equal(Color.FromRgb(0x11, 0x1B, 0x28), fieldColor);
            Assert.NotEqual(Colors.Black, Assert.IsType<SolidColorBrush>(moneyField.Foreground).Color);

            var editorScroll = Assert.IsType<ScrollViewer>(dialog.FindName("EditorFieldsScrollViewer"));
            Assert.Equal(1, Grid.GetRow(editorScroll));
            Assert.Equal(ScrollBarVisibility.Auto, editorScroll.VerticalScrollBarVisibility);
            var footer = Assert.IsType<Button>(dialog.FindName("SkillsApplyFooterButton"));
            Assert.Equal(action.ActionId == "skills" ? Visibility.Visible : Visibility.Collapsed,
                footer.Visibility);
            Assert.InRange(dialog.Height, dialog.MinHeight, dialog.MaxHeight);
            // Do not call Show(), ShowDialog(), or create an HWND inside the test host:
            // those can require a real interactive message pump on hosted CI runners.
            dialog.Close();
        }
    });

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
