using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NmcScsLauncher.App;

/// <summary>Use the system-drawn dark caption without changing WPF window chrome.
/// Unsupported Windows/DWM variants keep their native caption instead of breaking the UI.</summary>
internal static class WindowsTitleBarTheme
{
    private const int UseImmersiveDarkMode = 20;
    private const int UseImmersiveDarkModeBeforeWindows20H1 = 19;

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10))
            return;

        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return; // SourceInitialized must run before applying the native caption.

        try
        {
            var enabled = 1;
            if (DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode,
                    ref enabled, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, UseImmersiveDarkModeBeforeWindows20H1,
                    ref enabled, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
