using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.Foundation.Collections;

namespace FindNeedleUX.Pages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class ResultsWebPage : Page
{
    public ResultsWebPage()
    {
        this.InitializeComponent();
        try
        {
            //MyWebView.Source = new Uri("ms-appx-web:///assets/www/index.html");
            Init();
        }
        catch (Exception)
        {
          
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        // Dispose and release the WebView2 control when navigating away
        try
        {
            if (MyWebView != null)
            {
                MyWebView.CoreWebView2?.Stop();
                MyWebView.Source = null;
                // No Dispose method, so set Source to null and remove event handlers
                MyWebView.NavigationCompleted -= null; // Remove all handlers if possible
                MyWebView.CoreWebView2.WebMessageReceived -= MessageReceived;
            }
        }
        catch { }
    }

    private async void Init()
    {
        try
        {
            await MyWebView.EnsureCoreWebView2Async();
            MyWebView.NavigationCompleted += (sender, e) =>
            {
                if (e.IsSuccess == false)
                {
                    Console.WriteLine($"Navigation failed: {e.WebErrorStatus}");
                }
                // Always send theme after navigation completes
                SendThemeToWebView();
                // Show devtools if debug mode is on
                if (FindPluginCore.GlobalConfiguration.GlobalSettings.Debug)
                {
                    try { MyWebView.CoreWebView2.OpenDevToolsWindow(); } catch { }
                }
            };

            MyWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "appassets", "WebContent", CoreWebView2HostResourceAccessKind.Allow);

            MyWebView.Source = new Uri("http://appassets/resultsweb.html");
            MyWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            MyWebView.CoreWebView2.WebMessageReceived += MessageReceived;
        }
        catch (Exception)
        {
           
        }
    }

    private void SendThemeToWebView()
    {
        // Send theme colors to the web page
        var backgroundBrush = Application.Current.Resources["ApplicationPageBackgroundThemeBrush"] as SolidColorBrush;
        var foregroundBrush = Application.Current.Resources["TextFillColorPrimaryBrush"] as SolidColorBrush;
        var bgHex = backgroundBrush != null ? ColorToHex(backgroundBrush) : "#FFFFFF";
        var fgHex = foregroundBrush != null ? ColorToHex(foregroundBrush) : "#000000";
        var colorMessage = new {
            verb = "setTheme",
            data = new {
                background = bgHex,
                foreground = fgHex
            }
        };
        try
        {
            MyWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(colorMessage));
        }
        catch { }
    }

    private static string ColorToHex(SolidColorBrush brush)
    {
        var color = brush.Color;
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private void MessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        LoadResults();
    }

    public static string SerializeAndEncodeLogLine(LogLine logLine)
    {
        // Dynamically get all public properties of LogLine
        var dict = new Dictionary<string, object?>();
        foreach (var prop in typeof(LogLine).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            var value = prop.GetValue(logLine);
            if (value is string str)
                dict[prop.Name] = System.Web.HttpUtility.JavaScriptStringEncode(str);
            else if (value != null)
                dict[prop.Name] = System.Web.HttpUtility.JavaScriptStringEncode(value.ToString());
            else
                dict[prop.Name] = "null";
        }
        // Also include all fields (not just properties)
        foreach (var field in typeof(LogLine).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            var value = field.GetValue(logLine);
            if (value is string str)
                dict[field.Name] = System.Web.HttpUtility.JavaScriptStringEncode(str);
            else if (value != null)
                dict[field.Name] = System.Web.HttpUtility.JavaScriptStringEncode(value.ToString());
            else
                dict[field.Name] = "null";
        }
        return JsonSerializer.Serialize(dict);
    }

    private void LoadResults()
    {
        List<LogLine> LogLineList = MiddleLayerService.GetLogLines();
        foreach (LogLine logLine in LogLineList)
        {
            var encodedLogLine = SerializeAndEncodeLogLine(logLine);

            // Deserialize back to an object so it can be embedded as a JSON object, not a string
            var logLineObj = JsonSerializer.Deserialize<Dictionary<string, object>>(encodedLogLine);

            var message = new
            {
                verb = "newresult",
                data = logLineObj
            };

            var messageJson = JsonSerializer.Serialize(message);
            MyWebView.CoreWebView2.PostWebMessageAsJson(messageJson);
        }

        var doneMessage = new
        {
            verb = "done",
            data = new
            {
                id = 0
            }
        };
        MyWebView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(doneMessage));
        SendThemeToWebView();
    }
}