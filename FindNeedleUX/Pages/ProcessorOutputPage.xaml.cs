using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using FindNeedlePluginLib;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;

namespace FindNeedleUX.Pages;

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
            var header = (processor as IResultProcessor)?.GetDescription() ?? processor.GetType().Name;
            var output = (processor as IResultProcessor)?.GetOutputText() ?? "No output.";

            var outputBox = new TextBox
            {
                Text = output,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var runButton = new Button
            {
                Content = "Run Processor",
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                Tag = new ProcessorButtonTag { Processor = processor, OutputBox = outputBox }
            };
            runButton.Click += RunProcessor_Click;

            var stack = new StackPanel();
            stack.Children.Add(runButton);
            stack.Children.Add(new ScrollViewer { Content = outputBox });

            var tab = new TabViewItem
            {
                Header = header,
                Content = stack
            };
            tabView.TabItems.Add(tab);
        }
    }

    private void RunProcessor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ProcessorButtonTag tag)
        {
            var outputBox = tag.OutputBox;
            if (tag.Processor is IResultProcessor processor && outputBox != null)
            {
                var results = MiddleLayerService.GetSearchResults();
                processor.ProcessResults(results);
                outputBox.Text = processor.GetOutputText();
            }
        }
    }

    private class ProcessorButtonTag
    {
        public object Processor { get; set; }
        public TextBox OutputBox { get; set; }
    }
}
