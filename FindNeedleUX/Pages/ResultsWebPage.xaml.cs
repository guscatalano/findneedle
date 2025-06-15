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
using Windows.UI.Core;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

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
            };

            MyWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "appassets", "WebContent", CoreWebView2HostResourceAccessKind.Allow);

            MyWebView.Source = new Uri("http://appassets/resultsweb.html");
            //LoadResults();
            MyWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
       
            MyWebView.CoreWebView2.WebMessageReceived += MessageReceived;
            //  MyWebView.NavigationCompleted += MyWebView_Loaded;


            MyWebView.CoreWebView2.OpenDevToolsWindow();
        }
        catch (Exception)
        {
           
        }
    }

    private void MessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        LoadResults();
    }

    public static string SerializeAndEncodeLogLine(LogLine logLine)
    {
        List<string> columnsToSend = ["Index", "Time", "Provider", "TaskName", "Message", "Source"];
        var dict = new Dictionary<string, object?>();
        foreach (var prop in typeof(LogLine).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if(columnsToSend.Contains(prop.Name) == false)
            {
                continue; // Skip properties not in the list
            }
            var value = prop.GetValue(logLine);
            if (value is string str)
                dict[prop.Name] = HttpUtility.JavaScriptStringEncode(str);
            else if (value != null)
                dict[prop.Name] = HttpUtility.JavaScriptStringEncode(value.ToString());
            else
                dict[prop.Name] = null;
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
    }
}
