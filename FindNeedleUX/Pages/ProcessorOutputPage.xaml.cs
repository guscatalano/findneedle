using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using FindNeedlePluginLib;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using Microsoft.UI.Text;
using System;
using FindNeedleCoreUtils;
using System.Diagnostics;
using System.IO;

namespace FindNeedleUX.Pages;

public sealed partial class ProcessorOutputPage : Page
{
    private List<object> _processors;
    private Dictionary<object, UIElement> _processorContentMap = new();

    public ProcessorOutputPage()
    {
        this.InitializeComponent();
        Loaded += ProcessorOutputPage_Loaded;
    }

    private void ProcessorOutputPage_Loaded(object sender, RoutedEventArgs e)
    {
        LoadProcessorNavItems();
    }

    private void LoadProcessorNavItems()
    {
        var navView = this.FindName("ProcessorNavView") as NavigationView;
        var contentGrid = this.FindName("ProcessorContentGrid") as Grid;
        if (navView == null || contentGrid == null) return;
        navView.MenuItems.Clear();
        contentGrid.Children.Clear();
        _processorContentMap.Clear();
        var query = MiddleLayerService.SearchQueryUX.CurrentQuery;
        var processors = query != null ? query.Processors : new List<IResultProcessor>();
        _processors = new List<object>(processors);
        if (_processors == null || _processors.Count == 0)
        {
            navView.MenuItems.Add(new NavigationViewItem { Content = "No Processors", IsEnabled = false });
            contentGrid.Children.Add(new TextBlock { Text = "No processors are enabled in this workspace." });
            return;
        }
        foreach (var processor in _processors)
        {
            var name = processor.GetType().Name;
            var description = (processor as IResultProcessor)?.GetDescription() ?? name;
            var outputText = (processor as IResultProcessor)?.GetOutputText() ?? "No output.";
            var outputFile = (processor as IResultProcessor)?.GetOutputFile(TempStorage.GetSingleton().tempPath);

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

            var titleBlock = new TextBlock
            {
                Text = name,
                FontWeight = FontWeights.Bold,
                FontSize = 20,
                Margin = new Thickness(0, 0, 0, 2)
            };
            var descBlock = new TextBlock
            {
                Text = description,
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.Gray),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };

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
            stack.Children.Add(titleBlock);
            stack.Children.Add(descBlock);
            stack.Children.Add(runButton);
            stack.Children.Add(pivot);

            _processorContentMap[processor] = stack;
            var navItem = new NavigationViewItem { Content = name, Tag = processor };
            ToolTipService.SetToolTip(navItem, description);
            navView.MenuItems.Add(navItem);
        }
        // Select the first processor by default
        if (navView.MenuItems.Count > 0)
        {
            navView.SelectedItem = navView.MenuItems[0];
            ShowProcessorContent(_processors[0]);
        }
    }

    private void ProcessorNavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag != null)
        {
            ShowProcessorContent(item.Tag);
            // Collapse the pane after selection
            sender.IsPaneOpen = false;
        }
    }

    private void ProcessorNavView_PaneOpened(NavigationView sender, object args)
    {
        foreach (var item in sender.MenuItems)
        {
            if (item is NavigationViewItem navItem)
                navItem.Visibility = Visibility.Visible;
        }
    }

    private void ProcessorNavView_PaneClosed(NavigationView sender, object args)
    {
        foreach (var item in sender.MenuItems)
        {
            if (item is NavigationViewItem navItem)
                navItem.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowProcessorContent(object processor)
    {
        var contentGrid = this.FindName("ProcessorContentGrid") as Grid;
        if (contentGrid == null) return;
        contentGrid.Children.Clear();
        if (_processorContentMap.TryGetValue(processor, out var content))
        {
            contentGrid.Children.Add(content);
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