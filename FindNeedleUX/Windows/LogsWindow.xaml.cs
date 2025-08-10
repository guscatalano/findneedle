using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using System;
using Windows.Graphics;
using Microsoft.UI;

namespace FindNeedleUX.Windows;

public sealed class LogsWindow : Window
{
    public LogsWindow()
    {
        var frame = new Frame();
        frame.Navigate(typeof(FindNeedleUX.Pages.LogsPage));
        this.Content = frame;
        this.Title = "Logs";
        // Set window size using AppWindow
        var appWindow = GetAppWindow();
        if (appWindow != null)
        {
            appWindow.Resize(new SizeInt32(800, 600));
        }
    }

    private AppWindow? GetAppWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }
}
