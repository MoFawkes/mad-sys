using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace AqiClock.App.Services;

internal static class WindowActivation
{
    internal static void ShowAndActivate(Window window)
    {
        window.Show();
        // Activate alone flashes a minimized window's taskbar button. SW_RESTORE
        // also preserves whether the window was maximized before minimizing.
        if (window.WindowState == WindowState.Minimized)
            ShowWindow(new WindowInteropHelper(window).Handle, 9);
        window.Activate();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);
}
