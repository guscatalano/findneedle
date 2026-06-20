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
    private List<FindNeedleRuleDSL.UmlDiagramUsage> _diagramUsages = new();

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

        _diagramUsages = (query as FindPluginCore.Searching.NuSearchQuery)?.GeneratedDiagramUsages?.ToList()
            ?? new List<FindNeedleRuleDSL.UmlDiagramUsage>();

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

        // Rich, explained detail for RuleDSL processors (the common case). Falls back to the plain
        // GetOutputText() summary for any other processor type.
        if (processor is FindNeedleRuleDSL.FindNeedleRuleDSLPlugin ruleDsl)
        {
            AddRuleDslDetail(panel, ruleDsl);
        }
        else
        {
            var summary = SafeGet(() => processor.GetOutputText());
            if (!string.IsNullOrWhiteSpace(summary))
                panel.Children.Add(SummaryBox(summary.Trim()));
        }

        var outputFile = SafeGet(() => processor.GetOutputFile(TempStorage.GetSingleton().tempPath));
        if (!string.IsNullOrWhiteSpace(outputFile) && File.Exists(outputFile))
        {
            panel.Children.Add(new TextBlock { Text = "Output file", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) });
            panel.Children.Add(BuildFileDetail(outputFile!, includeTitle: false));
        }

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    private static Border SummaryBox(string text) => new Border
    {
        Margin = new Thickness(0, 8, 0, 0),
        Padding = new Thickness(12),
        CornerRadius = new CornerRadius(6),
        Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
        Child = new TextBlock { Text = text, FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
    };

    /// <summary>Explain a RuleDSL processor's run: how many rows it matched out of how many scanned,
    /// the rule file, tag breakdown, and the matched rows grouped by the rule that fired (with content)
    /// — so "found N results" isn't an opaque number.</summary>
    private void AddRuleDslDetail(StackPanel panel, FindNeedleRuleDSL.FindNeedleRuleDSLPlugin ruleDsl)
    {
        var matched = ruleDsl.MatchDetails ?? new List<FindNeedleRuleDSL.RuleMatchDetail>();
        var input = ruleDsl.LastInputCount;
        var ruleCount = ruleDsl.RuleCount;

        // Headline stat.
        panel.Children.Add(new TextBlock
        {
            Text = input > 0
                ? $"Matched {ruleDsl.MatchedCount} of {input} scanned rows  ·  {ruleCount} rule(s) in this file"
                : $"Matched {ruleDsl.MatchedCount} rows  ·  {ruleCount} rule(s) in this file",
            FontSize = 14,
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(ruleDsl.RulesFilePath))
        {
            var link = new HyperlinkButton { Content = ruleDsl.RulesFilePath, Padding = new Thickness(0), FontSize = 11 };
            link.Click += (_, _) => { try { Process.Start(new ProcessStartInfo { FileName = ruleDsl.RulesFilePath, UseShellExecute = true }); } catch { } };
            panel.Children.Add(link);
        }

        // Tag breakdown.
        var tags = ruleDsl.TagCounts;
        if (tags != null && tags.Count > 0)
        {
            var tagPanel = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 0) };
            foreach (var kv in tags.OrderByDescending(x => x.Value))
                tagPanel.Children.Add(new TextBlock { Text = $"• {kv.Key}: {kv.Value}", FontSize = 13 });
            panel.Children.Add(new Expander
            {
                Header = $"Tags applied: {tags.Count}",
                IsExpanded = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 8, 0, 0),
                Content = tagPanel,
            });
        }

        if (matched.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No rows matched — nothing to show. The rule file's match patterns didn't hit any scanned row.",
                Foreground = new SolidColorBrush(Colors.Gray),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
            });
            return;
        }

        // Matched rows grouped by the rule that fired.
        foreach (var grp in matched.GroupBy(m => m.RuleName).OrderByDescending(g => g.Count()))
        {
            var items = grp.ToList();
            var first = items[0];
            var lines = new StackPanel { Spacing = 3, Margin = new Thickness(20, 2, 0, 4) };
            if (!string.IsNullOrWhiteSpace(first.RuleMatch))
                lines.Children.Add(new TextBlock { Text = $"match: {first.RuleMatch}", FontSize = 11, FontFamily = new FontFamily("Consolas"), Foreground = new SolidColorBrush(Colors.Gray), TextWrapping = TextWrapping.Wrap });
            foreach (var m in items)
            {
                lines.Children.Add(new TextBlock
                {
                    Text = m.Content,
                    FontSize = 12,
                    FontFamily = new FontFamily("Consolas"),
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                });
            }

            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            head.Children.Add(new SymbolIcon { Symbol = Symbol.Accept, Foreground = new SolidColorBrush(Color.FromArgb(255, 46, 160, 67)) });
            head.Children.Add(new TextBlock { Text = grp.Key, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var actionType = string.IsNullOrWhiteSpace(first.ActionType) ? "" : $"[{first.ActionType}] ";
            head.Children.Add(new TextBlock { Text = $"{actionType}x{items.Count}", Foreground = new SolidColorBrush(Colors.Gray), VerticalAlignment = VerticalAlignment.Center });

            panel.Children.Add(new Expander
            {
                Header = head,
                IsExpanded = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 4, 0, 0),
                Content = lines,
            });
        }
    }

    private UIElement BuildFileDetail(string path, bool includeTitle = true)
    {
        // Output paths can mix separators (e.g. "...\win-x64\output/file.mmd" from {output}/{datetime}).
        // explorer /select fails on a forward slash and silently opens the desktop, so normalize first.
        try { path = Path.GetFullPath(path); } catch { /* keep original if it can't be normalized */ }

        // Common toolbar (file actions). For .mmd we also offer "Open rendered diagram" which opens
        // the generated zoomable HTML in the browser for a full-screen view.
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (IsMermaid(path) || IsImageFile(path))
        {
            var renderBtn = new Button { Content = WithSymbol(Symbol.View, "Open rendered (browser)") };
            renderBtn.Click += (_, _) =>
            {
                try
                {
                    var html = IsMermaid(path) ? GenerateMermaidHtml(path) : GenerateImageHtml(path);
                    TryStart(new ProcessStartInfo { FileName = html, UseShellExecute = true });
                }
                catch { }
            };
            actions.Children.Add(renderBtn);
        }
        var openBtn = new Button { Content = WithSymbol(Symbol.OpenFile, "Open source") };
        openBtn.Click += (_, _) => TryStart(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        var revealBtn = new Button { Content = WithSymbol(Symbol.Folder, "Show in folder") };
        revealBtn.Click += (_, _) => TryStart(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true });
        actions.Children.Add(openBtn);
        actions.Children.Add(revealBtn);

        // --- Visual (diagram / image) ---
        // WebView2 has an "airspace" limitation: inside a ScrollViewer it renders ON TOP of the
        // scrollbar and won't scroll. So the metadata (title, actions, rules-used) goes in its own
        // scrollable row, and the diagram sits in a separate star row below — never inside a
        // ScrollViewer. The diagram is always visible; expanding the rules scrolls within the top row.
        if (IsMermaid(path) || IsImageFile(path))
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });               // 0: scrollable metadata
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 1: diagram

            var meta = new StackPanel { Spacing = 6 };
            if (includeTitle)
                meta.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            actions.Margin = new Thickness(0, 0, 0, 8);
            meta.Children.Add(actions);
            var rulesPanel = BuildRulesUsedPanel(path);
            if (rulesPanel != null) meta.Children.Add(rulesPanel);

            var metaScroll = new ScrollViewer
            {
                Content = meta,
                MaxHeight = 280, // cap the metadata; the diagram below always stays visible
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 0, 0, 8),
            };
            grid.Children.Add(metaScroll);
            Grid.SetRow(metaScroll, 0);

            UIElement view;
            try
            {
                var wv = new WebView2 { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
                var htmlPath = IsMermaid(path) ? GenerateMermaidHtml(path) : GenerateImageHtml(path);
                wv.Source = new Uri("file:///" + htmlPath.Replace("\\", "/"));
                view = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    MinHeight = 280,
                    Child = wv,
                };
            }
            catch (Exception ex)
            {
                view = new TextBlock { Text = $"(unable to display: {ex.Message})", Foreground = new SolidColorBrush(Colors.Gray) };
            }
            grid.Children.Add(view);
            Grid.SetRow((FrameworkElement)view, 1);
            return grid;
        }

        // --- Text file: scrollable ---
        var panel = new StackPanel { Spacing = 6 };
        if (includeTitle)
            panel.Children.Add(new TextBlock { Text = Path.GetFileName(path), FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(actions);
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
        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    /// <summary>Collapsible panel listing which UML rules fired for this diagram (with counts) and
    /// which never matched. Returns null when there's no usage info for the file (e.g. an image).</summary>
    private FrameworkElement? BuildRulesUsedPanel(string path)
    {
        FindNeedleRuleDSL.UmlDiagramUsage? usage = null;
        foreach (var u in _diagramUsages)
        {
            if (PathsEqual(u.FilePath, path)) { usage = u; break; }
        }
        if (usage == null || usage.Rules.Count == 0) return null;

        var used = usage.Rules.Where(r => r.Count > 0).ToList();
        var unused = usage.Rules.Where(r => r.Count == 0).ToList();

        var list = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var r in used)
        {
            // Header row: check + rule name + count.
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            head.Children.Add(new SymbolIcon { Symbol = Symbol.Accept, Foreground = new SolidColorBrush(Color.FromArgb(255, 46, 160, 67)) });
            head.Children.Add(new TextBlock { Text = r.Name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(new TextBlock { Text = $"x{r.Count}", Foreground = new SolidColorBrush(Colors.Gray), VerticalAlignment = VerticalAlignment.Center });

            if (r.Lines != null && r.Lines.Count > 0)
            {
                // Expandable: the actual lines this rule picked up.
                var lines = new StackPanel { Spacing = 1, Margin = new Thickness(20, 2, 0, 4) };
                foreach (var ln in r.Lines)
                {
                    lines.Children.Add(new TextBlock
                    {
                        Text = ln.Content,
                        FontSize = 12,
                        FontFamily = new FontFamily("Consolas"),
                        Foreground = new SolidColorBrush(Colors.Gray),
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                    });
                }
                list.Children.Add(new Expander
                {
                    Header = head,
                    IsExpanded = false,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Content = lines,
                });
            }
            else
            {
                list.Children.Add(head);
            }
        }
        foreach (var r in unused)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Opacity = 0.55, Margin = new Thickness(0, 2, 0, 0) };
            row.Children.Add(new SymbolIcon { Symbol = Symbol.Remove, Foreground = new SolidColorBrush(Colors.Gray) });
            row.Children.Add(new TextBlock { Text = r.Name, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = "unused", Foreground = new SolidColorBrush(Colors.Gray), VerticalAlignment = VerticalAlignment.Center });
            list.Children.Add(row);
        }

        // The whole detail pane scrolls, so the panel can grow naturally (no inner scroll cap —
        // a nested scrollbar inside the outer one is awkward).
        return new Expander
        {
            Header = $"Rules used: {used.Count} of {usage.Rules.Count}",
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 8),
            Content = list,
        };
    }

    private static bool PathsEqual(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
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
        var encoded = System.Net.WebUtility.HtmlEncode(mermaidText);
        var html =
            "<!DOCTYPE html><html><head><meta charset='utf-8'>" +
            "<script src='https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.min.js'></script>" +
            "<style>" +
            "html,body{margin:0;height:100%;overflow:hidden;font-family:'Segoe UI',sans-serif;background:#fff;}" +
            "#stage{position:absolute;inset:0;overflow:hidden;cursor:grab;}#stage.grab{cursor:grabbing;}" +
            "#diagram{position:absolute;left:0;top:0;transform-origin:0 0;padding:12px;}" +
            "#diagram svg{max-width:none!important;height:auto!important;}" +
            ".controls{position:fixed;top:12px;right:12px;z-index:10;display:flex;gap:4px;background:#ffffffd0;border:1px solid #ddd;border-radius:8px;padding:6px;}" +
            ".controls button{width:34px;height:34px;font-size:16px;cursor:pointer;border:none;background:#eee;border-radius:6px;}" +
            ".controls button:hover{background:#ddd;}" +
            "</style></head><body>" +
            "<div id='stage'><div id='diagram'><pre class='mermaid'>" + encoded + "</pre></div></div>" +
            "<div class='controls'><button id='zin' title='Zoom in'>+</button><button id='zout' title='Zoom out'>−</button><button id='zfit' title='Fit'>Fit</button><button id='zone' title='Actual size'>1:1</button></div>" +
            "<script>" +
            "mermaid.initialize({startOnLoad:true,securityLevel:'loose'});" +
            "let s=1,tx=12,ty=12,drag=false,sx=0,sy=0;" +
            "const stage=document.getElementById('stage'),dia=document.getElementById('diagram');" +
            "function apply(){dia.style.transform='translate('+tx+'px,'+ty+'px) scale('+s+')';}" +
            "function fit(){const svg=dia.querySelector('svg');if(!svg)return;const w=svg.clientWidth||svg.getBoundingClientRect().width,h=svg.clientHeight||svg.getBoundingClientRect().height;if(!w||!h)return;s=Math.min((stage.clientWidth-24)/w,(stage.clientHeight-24)/h,1);tx=12;ty=12;apply();}" +
            "document.getElementById('zin').onclick=()=>{s=Math.min(s*1.2,8);apply();};" +
            "document.getElementById('zout').onclick=()=>{s=Math.max(s/1.2,0.1);apply();};" +
            "document.getElementById('zfit').onclick=fit;" +
            "document.getElementById('zone').onclick=()=>{s=1;tx=12;ty=12;apply();};" +
            "stage.addEventListener('wheel',e=>{e.preventDefault();const f=e.deltaY<0?1.1:1/1.1;const r=stage.getBoundingClientRect(),mx=e.clientX-r.left,my=e.clientY-r.top;tx=mx-(mx-tx)*f;ty=my-(my-ty)*f;s=Math.max(0.1,Math.min(s*f,8));apply();},{passive:false});" +
            "stage.addEventListener('mousedown',e=>{drag=true;sx=e.clientX-tx;sy=e.clientY-ty;stage.classList.add('grab');});" +
            "window.addEventListener('mousemove',e=>{if(drag){tx=e.clientX-sx;ty=e.clientY-sy;apply();}});" +
            "window.addEventListener('mouseup',()=>{drag=false;stage.classList.remove('grab');});" +
            "const obs=new MutationObserver(()=>{if(dia.querySelector('svg')){obs.disconnect();setTimeout(fit,60);}});obs.observe(dia,{childList:true,subtree:true});" +
            "</script></body></html>";
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
