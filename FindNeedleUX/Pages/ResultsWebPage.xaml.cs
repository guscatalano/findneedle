using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
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
        catch (Exception ex)
        {
            int i = 0;
        }
    }

    private async void Init()
    {
        try
        {
            await MyWebView.EnsureCoreWebView2Async();

            MyWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "appassets", "WebContent", CoreWebView2HostResourceAccessKind.Allow);

            MyWebView.Source = new Uri("http://appassets/resultsweb.html");
            //LoadResults();
            MyWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
       
            MyWebView.CoreWebView2.WebMessageReceived += MessageReceived;
            //  MyWebView.NavigationCompleted += MyWebView_Loaded;


       //     MyWebView.CoreWebView2.OpenDevToolsWindow();
        }
        catch (Exception)
        {
            int i = 0;
        }
    }

    private void MessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        LoadResults();
    }

   

    private void LoadResults()
    {
        List<LogLine> LogLineList = MiddleLayerService.GetLogLines();
        // Thread.Sleep(10000);
        foreach(LogLine logLine in LogLineList) {
            string encodedline = System.Web.HttpUtility.JavaScriptStringEncode(logLine.Message);
            MyWebView.CoreWebView2.PostWebMessageAsJson("{\"verb\":\"newresult\",\"data\":\"id: "+logLine.Index + " msg: " + encodedline + "\"}");
            //Thread.Sleep(1000);
        
        }
    }
}
