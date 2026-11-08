using System.Windows;
using System.Windows.Threading;
using NmcScsLauncher.App.ViewModels;
using Xunit;

namespace NmcScsLauncher.App.Tests;

/// <summary>Exercises the actual compiled WPF dialog, not only its raw XAML markup.
/// No game process, LicenseHub startup, profile or save I/O is required.
/// </summary>
public sealed class SaveEditorActionDialogRuntimeTests
{
    [Fact]
    public Task AllElevenDialogsCanBeConstructedShownAndClosedWithoutASave() => OnStaThread(() =>
    {
        // Initialize only WPF application resources. Do not call Run/OnStartup:
        // that path would initialize LicenseHub and game detection.
        if (Application.Current is null)
        {
            var app = new NmcScsLauncher.App.App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }

        var viewModel = new SaveEditorTestHarness().ViewModel;
        foreach (var action in viewModel.SaveEditorOverviewActions)
        {
            using var _ = new DialogCloser();
            var dialog = new SaveEditorActionDialog(viewModel, action);
            try
            {
                dialog.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
                dialog.UpdateLayout();
                Assert.True(dialog.IsLoaded, action.ActionId);
                Assert.Equal(action.Title + " – NMC SCS LAUNCHER", dialog.Title);
            }
            finally { dialog.Close(); }
        }
    });

    private sealed class DialogCloser : IDisposable
    {
        public void Dispose() { }
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
