using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.Generic;
using System.Linq;
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
using Windows.UI;

namespace FindNeedleUX.Pages;

public sealed partial class ProcessorOutputPage : Page
{
    private enum EntryKind { Header, Processor, File }

    private sealed class OutputEntry
    {
        public EntryKind Kind;
        public string Title = "";
        public string Subtitle = "";
        public IResultProcessor? Processor;
        public string? FilePath;
    }

    public ProcessorOutputPage()
    {
        this.InitializeComponent();
        Loaded += (_, _) => BuildPage();
    }

    private void BuildPage()
    {
        OutputList.Items.Clear();
        DetailHost.Children.Clear();

        var query = MiddleLayerService.SearchQueryUX.CurrentQuery;
        var processors = (query?.Processors ?? new List<IResultProcessor>()).ToList();

        // Gather rule-output files from both the captured static and the live query — different run
        // entry points populate one or the other, so read both to be robust.
        var files = new List<string>();
        if (MiddleLayerService.LastRuleOutputFiles != null) files.AddRange(MiddleLayerService.LastRuleOutputFiles);
        if (query is FindPluginCore.Searching.NuSearchQuery nq && nq.GeneratedRuleOutputFiles != null)
            files.AddRange(nq.GeneratedRuleOutputFiles);
        files = files.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Summary line.
        var diagramCount = files.Count(IsMermaid);
        var parts = new List<string>
        {
            $"{processors.Count} rule{(processors.Count == 1 ? "" : "s")}/processor{(processors.Count == 1 ? "" : "s")} applied"
        };
        if (files.Count > 0) parts.Add($"{files.Count} file{(files.Count == 1 ? "" : "s")} generated");
        if (diagramCount > 0) parts.Add($"{diagramCount} diagram{(diagramCount == 1 ? "" : "s")}");
        SummaryText.Text = string.Join("  -  ", parts);

        ListViewItem? firstSelectable = null;

        // --- Generated files first (the payoff: diagrams / exports) ---
        if (files.Count > 0)
        {
            OutputList.Items.Add(MakeHeaderItem("Generated files"));
            foreach (var f in files)
            {
                var entry = new OutputEntry
                {
                    Kind = EntryKind.File,
                    Title = Path.GetFileName(f),
                    Subtitle = DescribeFile(f),
                    FilePath = f,
                };
                var item = MakeEntryItem(entry, SymbolFor(f));
                OutputList.Items.Add(item);
                if (firstSelectable == null && (IsMermaid(f) || IsImageFile(f))) firstSelectable = item;
            }
        }

        // --- Rules & processors ---
        if (processors.Count > 0)
        {
            OutputList.Items.Add(MakeHeaderItem("Rules & processors"));
            foreach (var p in processors)
            {
                var (title, subtitle) = DescribeProcessor(p);
                var entry = new OutputEntry
                {
                    Kind = EntryKind.Processor,
                    Title = title,
                    Subtitle = subtitle,
                    Processor = p,
                };
                var item = MakeEntryItem(entry, Symbol.Setting);
                OutputList.Items.Add(item);
                firstSelectable ??= item;
            }
        }

        if (firstSelectable == null)
        {
            ShowEmptyDetail();
            return;
        }

        OutputList.SelectedItem = firstSelectable;
    }

    // ---------- list item factories ----------

    private static ListViewItem MakeHeaderItem(string text)
    {
        return new ListViewItem
        {
            Content = new TextBlock
            {
                Text = text.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.Gray),
                Margin = new Thickness(4, 8, 4, 2),
            },
            IsHitTestVisible = false,
            IsEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
    }

    private static ListViewItem MakeEntryItem(OutputEntry entry, Symbol symbol)
    {
        var icon = new SymbolIcon
        {
            Symbol = symbol,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 10, 0),
        };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = entry.Title, TextTrimming = TextTrimming.CharacterEllipsis });
        if (!string.IsNullOrEmpty(entry.Subtitle))
        {
            text.Children.Add(new TextBlock
            {
                Text = entry.Subtitle,
                FontSize = 11,
                Foreground = new SolidColorBrush(Colors.Gray),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(text);

        return new ListViewItem
        {
            Content = row,
            Tag = entry,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
    }

    // ---------- selection ----------

    private void OutputList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutputList.SelectedItem is ListViewItem lvi && lvi.Tag is OutputEntry entry)
        {
            DetailHost.Children.Clear();
            UIElement content = entry.Kind == EntryKind.File
                ? BuildFileDetail(entry.FilePath!)
                : BuildProcessorDetail(entry.Processor!, entry.Title);
            DetailHost.Children.Add(content);
        }
    }

    private void ShowEmptyDetail()
    {
        DetailHost.Children.Clear();
        DetailHost.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                new SymbolIcon { Symbol = Symbol.Help, Foreground = new SolidColorBrush(Colors.Gray), HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "No processor output", FontSize = 16, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center },
                new TextBlock { Text = "Run a search with rules, processors, or output rules enabled.", FontSize = 13, Foreground = new SolidColorBrush(Colors.Gray), HorizontalAlignment = HorizontalAlignment.Center },
            },
        });
    }

    // ---------- detail builders ----------

    private UIElement BuildProcessorDetail(IResultProcessor processor, string title)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold });

        var desc = SafeGet(() => processor.GetDescription());
        if (!string.IsNullOrWhiteSpace(desc))
            panel.Children.Add(new TextBlock { Text = desc, FontSize = 13, Foreground = new SolidColorBrush(Colors.Gray), TextWrapping = TextWrapping.Wrap });

        var summary = SafeGet(() => processor.GetOutputText());
        if (!string.IsNullOrWhiteSpace(summary))
        {
            panel.Children.Add(new Border
            {
                Margin = new Thickness(0, 8, 0, 0),
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
                Child = new TextBlock { Text = summary.Trim(), FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            });
        }

        var outputFile = SafeGet(() => processor.GetOutputFile(TempStorage.GetSingleton().tempPath));
        if (!string.IsNullOrWhiteSpace(outputFile) && File.Exists(outputFile))
        {
            panel.Children.Add(new TextBlock { Text = "Output file", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
            panel.Children.Add(BuildFileDetail(outputFile!, includeTitle: false));
        }

        return panel;
    }

    private UIElement BuildFileDetail(string path, bool includeTitle = true)
    {
        var panel = new StackPanel { Spacing = 6 };

        if (includeTitle)
            panel.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontSize = 20, FontWeight = FontWeights.SemiBold });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 4) };
        var openBtn = new Button { Content = WithSymbol(Symbol.OpenFile, "Open") };
        openBtn.Click += (_, _) => TryStart(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        var revealBtn = new Button { Content = WithSymbol(Symbol.Folder, "Show in folder") };
        revealBtn.Click += (_, _) => TryStart(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true });
        actions.Children.Add(openBtn);
        actions.Children.Add(revealBtn);
        panel.Children.Add(actions);

        panel.Children.Add(new TextBlock
        {
            Text = path,
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.Gray),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });

        try
        {
            if (IsMermaid(path) || IsImageFile(path))
            {
                var wv = new WebView2
                {
                    MinHeight = 460,
                    Height = 520,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Margin = new Thickness(0, 6, 0, 0),
                };
                var htmlPath = IsMermaid(path) ? GenerateMermaidHtml(path) : GenerateImageHtml(path);
                wv.Source = new Uri("file:///" + htmlPath.Replace("\\", "/"));
                panel.Children.Add(wv);

                if (IsMermaid(path))
                {
                    panel.Children.Add(new Expander
                    {
                        Header = "Diagram source (Mermaid)",
                        Margin = new Thickness(0, 8, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                        Content = new TextBox
                        {
                            Text = SafeReadAll(path),
                            IsReadOnly = true,
                            TextWrapping = TextWrapping.Wrap,
                            AcceptsReturn = true,
                            FontFamily = new FontFamily("Consolas"),
                            MinHeight = 120,
                        },
                    });
                }
            }
            else
            {
                panel.Children.Add(new TextBox
                {
                    Text = SafeReadAll(path),
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    AcceptsReturn = true,
                    FontFamily = new FontFamily("Consolas"),
                    MinHeight = 300,
                    Margin = new Thickness(0, 6, 0, 0),
                });
            }
        }
        catch (Exception ex)
        {
            panel.Children.Add(new TextBlock { Text = $"(unable to display: {ex.Message})", Foreground = new SolidColorBrush(Colors.Gray) });
        }

        return panel;
    }

    // ---------- helpers ----------

    private static (string title, string subtitle) DescribeProcessor(IResultProcessor p)
    {
        if (p is FindNeedleRuleDSL.FindNeedleRuleDSLPlugin ruleDsl && !string.IsNullOrWhiteSpace(ruleDsl.RulesFilePath))
        {
            var name = Path.GetFileName(ruleDsl.RulesFilePath);
            if (name.EndsWith(".rules.json", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - ".rules.json".Length);
            return ($"Rules: {name}", "RuleDSL");
        }
        var friendly = SafeGet(() => p.GetDescription()) is { Length: > 0 } d ? d : p.GetType().Name;
        return (friendly, p.GetType().Name);
    }

    private static string DescribeFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');
        if (IsMermaid(path)) return "Mermaid diagram";
        if (IsImageFile(path)) return "Image";
        return string.IsNullOrEmpty(ext) ? "File" : ext.ToUpperInvariant();
    }

    private static Symbol SymbolFor(string path)
    {
        if (IsImageFile(path)) return Symbol.Pictures;
        return Symbol.Document;
    }

    private static StackPanel WithSymbol(Symbol symbol, string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sp.Children.Add(new SymbolIcon { Symbol = symbol });
        sp.Children.Add(new TextBlock { Text = text });
        return sp;
    }

    private static bool IsMermaid(string path) => Path.GetExtension(path).Equals(".mmd", StringComparison.OrdinalIgnoreCase);

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".svg";
    }

    private static string SafeReadAll(string path)
    {
        try { return File.ReadAllText(path); } catch (Exception ex) { return $"(unable to read file: {ex.Message})"; }
    }

    private static string? SafeGet(Func<string?> get)
    {
        try { return get(); } catch { return null; }
    }

    private static void TryStart(ProcessStartInfo psi)
    {
        try { Process.Start(psi); } catch { }
    }

    /// <summary>Wrap a Mermaid .mmd file in an HTML page rendered via mermaid.js (CDN). The raw source
    /// stays available in the "Diagram source" expander / the file link if offline.</summary>
    private static string GenerateMermaidHtml(string mmdPath)
    {
        var mermaidText = File.ReadAllText(mmdPath);
        var htmlPath = Path.Combine(Path.GetDirectoryName(mmdPath)!, Path.GetFileNameWithoutExtension(mmdPath) + "_view.html");
        var html =
            "<!DOCTYPE html><html><head><meta charset='utf-8'>" +
            "<script src='https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.min.js'></script>" +
            "<script>mermaid.initialize({ startOnLoad: true, securityLevel: 'loose' });</script>" +
            "<style>body{margin:0;padding:12px;font-family:'Segoe UI',sans-serif;background:#ffffff;}</style>" +
            "</head><body>" +
            "<pre class='mermaid'>" + System.Net.WebUtility.HtmlEncode(mermaidText) + "</pre>" +
            "</body></html>";
        File.WriteAllText(htmlPath, html);
        return htmlPath;
    }

    private static string GenerateImageHtml(string imagePath)
    {
        var htmlPath = Path.Combine(Path.GetDirectoryName(imagePath)!, Path.GetFileNameWithoutExtension(imagePath) + "_view.html");
        var imgSrc = Path.GetFileName(imagePath);
        var html =
            "<html><head><style>" +
            "body{margin:0;padding:0;background:#fff;}" +
            ".img-zoom-container{position:relative;width:100vw;height:100vh;overflow:auto;}" +
            ".img-zoom{max-width:100vw;max-height:100vh;display:block;margin:auto;transition:transform .2s;cursor:grab;}" +
            ".zoom-controls{position:fixed;top:16px;right:16px;z-index:10;background:#fff8;border-radius:8px;padding:8px;}" +
            ".zoom-btn{font-size:24px;margin:4px;padding:4px 12px;cursor:pointer;border:none;background:#eee;border-radius:4px;}" +
            "</style></head><body>" +
            "<div class='img-zoom-container'><img src='" + imgSrc + "' class='img-zoom' id='zoomImg'/></div>" +
            "<div class='zoom-controls'><button class='zoom-btn' id='zoomIn'>+</button><button class='zoom-btn' id='zoomOut'>-</button><button class='zoom-btn' id='zoomReset'>Reset</button></div>" +
            "<script type='text/javascript'>" +
            "let zoom=1,originX=0,originY=0,isDragging=false,startX=0,startY=0;" +
            "const img=document.getElementById('zoomImg');" +
            "document.getElementById('zoomIn').onclick=function(){zoom=Math.min(zoom+0.2,5);apply();};" +
            "document.getElementById('zoomOut').onclick=function(){zoom=Math.max(zoom-0.2,1);if(zoom===1){originX=0;originY=0;}apply();};" +
            "document.getElementById('zoomReset').onclick=function(){zoom=1;originX=0;originY=0;apply();};" +
            "function apply(){img.style.transform='scale('+zoom+') translate('+originX+'px,'+originY+'px)';}" +
            "img.addEventListener('mousedown',function(e){if(zoom>1){isDragging=true;startX=e.clientX-originX;startY=e.clientY-originY;img.style.cursor='grabbing';}});" +
            "window.addEventListener('mousemove',function(e){if(isDragging){originX=e.clientX-startX;originY=e.clientY-startY;apply();}});" +
            "window.addEventListener('mouseup',function(){isDragging=false;img.style.cursor='grab';});" +
            "</script></body></html>";
        File.WriteAllText(htmlPath, html);
        return htmlPath;
    }
}
