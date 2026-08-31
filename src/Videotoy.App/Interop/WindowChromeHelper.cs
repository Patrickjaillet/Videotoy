using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Videotoy.App.Interop;

public static class WindowChromeHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    private const int DWMSBT_MAINWINDOW = 2;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void ApplyMicaBackdrop(Window window)
    {
        var helper = new WindowInteropHelper(window);
        var hwnd = helper.EnsureHandle();

        var darkMode = 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

        var backdropType = DWMSBT_MAINWINDOW;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));

        var cornerPreference = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
    }
}
