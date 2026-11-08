using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
