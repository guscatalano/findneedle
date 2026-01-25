using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Windows
{
    public sealed partial class DemoViewerWindow : Window
    {
        public DemoViewerWindow(string? htmlFilePath = null)
        {
            this.InitializeComponent();
            if (!string.IsNullOrEmpty(htmlFilePath))
            {
                try
                {
                    var uri = new System.Uri("file:///" + htmlFilePath.Replace('\\', '/'));
                    DemoWebView.Source = uri;
                }
                catch { }
            }
        }
    }
}
