using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using FindNeedlePluginLib;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using FindNeedleCoreUtils;
using System.Diagnostics;
using System.IO;

namespace FindNeedleUX.Pages;

public sealed partial class ProcessorOutputPage : Page
{
    public ProcessorOutputPage()
    {
        this.InitializeComponent();
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
            var outputText = (processor as IResultProcessor)?.GetOutputText() ?? "No output.";
            var outputFile = processor.GetOutputFile(TempStorage.GetSingleton().tempPath);

            var outputBox = new TextBox
            {
                Text = outputText,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                Margin = new Thickness(0, 8, 0, 0)
            };

            var webView = new WebView2
            {
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                MinHeight = 300,
                MinWidth = 300
            };
            if (!string.IsNullOrWhiteSpace(outputFile) && File.Exists(outputFile))
            {
                Debug.WriteLine($"[ProcessorOutputPage] Output file: {outputFile}");
                if (IsImageFile(outputFile))
                {
                    var htmlPath = GenerateImageHtml(outputFile);
                    webView.Source = new Uri("file:///" + htmlPath.Replace("\\", "/"));
                }
                else
                {
                    webView.Source = new Uri("file:///" + outputFile.Replace("\\", "/"));
                }
            }

            var runButton = new Button
            {
                Content = "Run Processor",
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                Tag = new ProcessorButtonTag { Processor = processor, OutputBox = outputBox, WebView = webView }
            };
            runButton.Click += RunProcessor_Click;

            var pivot = new Pivot();
            pivot.Items.Add(new PivotItem
            {
                Header = "Text Output",
                Content = new ScrollViewer { Content = outputBox }
            });
            pivot.Items.Add(new PivotItem
            {
                Header = "File Output",
                Content = webView
            });

            var stack = new StackPanel();
            stack.Children.Add(runButton);
            stack.Children.Add(pivot);

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
            if (tag.Processor is IResultProcessor processor && tag.OutputBox != null && tag.WebView != null)
            {
                var results = MiddleLayerService.GetSearchResults();
                processor.ProcessResults(results);
                tag.OutputBox.Text = processor.GetOutputText();
                var outputFile = processor.GetOutputFile(TempStorage.GetSingleton().tempPath);
                if (!string.IsNullOrWhiteSpace(outputFile) && File.Exists(outputFile))
                {
                    Debug.WriteLine($"[ProcessorOutputPage] Output file: {outputFile}");
                    if (IsImageFile(outputFile))
                    {
                        var htmlPath = GenerateImageHtml(outputFile);
                        tag.WebView.Source = new Uri("file:///" + htmlPath.Replace("\\", "/"));
                    }
                    else
                    {
                        tag.WebView.Source = new Uri("file:///" + outputFile.Replace("\\", "/"));
                    }
                }
                else
                {
                    tag.WebView.Source = null;
                }
            }
        }
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp";
    }

    private static string GenerateImageHtml(string imagePath)
    {
        var htmlPath = Path.Combine(Path.GetDirectoryName(imagePath)!, Path.GetFileNameWithoutExtension(imagePath) + "_view.html");
        var imgSrc = Path.GetFileName(imagePath);
        var html = $"<html><body style='margin:0;padding:0;'><img src='{imgSrc}' style='max-width:100vw;max-height:100vh;display:block;margin:auto;'/></body></html>";
        File.WriteAllText(htmlPath, html);
        return htmlPath;
    }

    private class ProcessorButtonTag
    {
        public object Processor { get; set; }
        public TextBox OutputBox { get; set; }
        public WebView2 WebView { get; set; }
    }
}