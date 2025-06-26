using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using findneedle.Interfaces;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;

namespace FindNeedleUX.Pages
{
    public sealed partial class ProcessorOutputPage : Page
    {
        public ProcessorOutputPage()
        {
            this.InitializeComponent(); // Restored for XAML initialization
            Loaded += ProcessorOutputPage_Loaded;
        }

        private void ProcessorOutputPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadProcessorTabs();
        }

        private void LoadProcessorTabs()
        {
            var tabView = this.FindName("ProcessorTabView") as TabView;
            if (tabView == null) return;
            tabView.TabItems.Clear();
            // Get all enabled processors (from MiddleLayerService or Query)
            var processors = MiddleLayerService.Query.Processors;
            if (processors == null || processors.Count == 0)
            {
                var tab = new TabViewItem { Header = "No Processors", Content = new TextBlock { Text = "No processors are enabled in this workspace." } };
                tabView.TabItems.Add(tab);
                return;
            }
            foreach (var processor in processors)
            {
                string header = (processor as IResultProcessor)?.GetDescription() ?? processor.GetType().Name;
                string output = (processor as IResultProcessor)?.GetOutputText() ?? "No output.";
                var tab = new TabViewItem
                {
                    Header = header,
                    Content = new ScrollViewer { Content = new TextBox { Text = output, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") } }
                };
                tabView.TabItems.Add(tab);
            }
        }
    }
}
