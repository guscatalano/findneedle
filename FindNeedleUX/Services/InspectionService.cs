using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FindNeedleCoreUtils;
using findneedle.ETWPlugin;
using findneedle.WDK;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace FindNeedleUX.Services;

/// Builds and shows the dialog flows for the Settings → Inspect ETL / Inspect Binary actions.
public static class InspectionService
{
    public static async Task InspectEtlAsync(Window window, Action<bool, string> showSpinner)
    {
        showSpinner(true, "Inspecting ETL file...");
        var hWnd = WindowNative.GetWindowHandle(window);
        var path = Win32FileDialog.OpenFile(hWnd, new (string, string)[] { ("ETL files", "*.etl") });
        if (path == null) { showSpinner(false, null); return; }

        EtlInfo info = null!;
        string error = null!;
        await Task.Run(() =>
        {
            try { info = EtlInfoExtractor.Inspect(path); }
            catch (Exception ex) { error = ex.Message; }
        });
        showSpinner(false, null);

        var dialog = new ContentDialog
        {
            Title = error == null ? "ETL Inspection Results" : "ETL Inspection Error",
            CloseButtonText = "OK",
            MinWidth = 700,
            XamlRoot = window.Content.XamlRoot
        };
        if (error != null)
        {
            dialog.Content = $"Error inspecting ETL: {error}";
        }
        else
        {
            var stack = new StackPanel();

            // Everything the engine extracted: file, Windows build, machine, capture window,
            // event/lost counts, format breakdown, and providers with per-provider counts.
            stack.Children.Add(new TextBlock
            {
                Text = EtlInfoExtractor.Format(info),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap
            });

            // Copy-out row: plaintext / JSON / XML / CSV to the clipboard.
            var copyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
            void AddCopy(string label, Func<string> getText)
            {
                var btn = new Button { Content = label };
                btn.Click += (_, _) =>
                {
                    try
                    {
                        var pkg = new DataPackage();
                        pkg.SetText(getText());
                        Clipboard.SetContent(pkg);
                    }
                    catch { /* clipboard contention — ignore */ }
                };
                copyRow.Children.Add(btn);
            }
            copyRow.Children.Add(new TextBlock { Text = "Copy as:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            AddCopy("Text", () => EtlInfoExtractor.ToPlainText(info));
            AddCopy("JSON", () => EtlInfoExtractor.ToJson(info));
            AddCopy("XML", () => EtlInfoExtractor.ToXml(info));
            AddCopy("CSV", () => EtlInfoExtractor.ToCsv(info));
            stack.Children.Add(copyRow);

            dialog.Content = new ScrollViewer { Content = stack, MaxHeight = 520 };
        }
        await dialog.ShowAsync();
    }

    public static async Task InspectBinaryAsync(Window window, Action<bool, string> showSpinner)
    {
        var warning = new ContentDialog
        {
            Title = "Inspect Binary (Experimental)",
            CloseButtonText = "Cancel",
            PrimaryButtonText = "Select File",
            XamlRoot = window.Content.XamlRoot
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = "Inspect Binary is experimental and may not find all ETW providers.",
            Foreground = new SolidColorBrush(Colors.OrangeRed),
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Reads the ETW providers a native EXE/DLL declares — from its WEVT_TEMPLATE manifest "
                 + "resource (authoritative GUIDs) and its TraceLogging provider metadata (names, with the "
                 + "GUID confirmed by the name→GUID hash). Each result is labelled with how it was found.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        stack.Children.Add(new TextBlock { Text = "Select a binary file to inspect for ETW providers." });
        warning.Content = stack;
        if (await warning.ShowAsync() != ContentDialogResult.Primary) return;

        showSpinner(true, "Inspecting binary file...");
        var hWnd = WindowNative.GetWindowHandle(window);
        var path = Win32FileDialog.OpenFile(hWnd, new (string, string)[] { ("Binary files", "*.exe;*.dll"), ("All files", "*.*") });
        if (path == null) { showSpinner(false, null); return; }

        List<EtwProviderRef> providers = null!;
        string error = null!;
        await Task.Run(() =>
        {
            try { providers = EtwNativeProviderScanner.Scan(path); }
            catch (Exception ex) { error = ex.Message; }
        });
        showSpinner(false, null);

        var dialog = new ContentDialog
        {
            Title = error == null ? "ETW Providers in Binary" : "ETW Provider Extraction Error",
            CloseButtonText = "OK",
            MinWidth = 640,
            XamlRoot = window.Content.XamlRoot
        };
        var content = new StackPanel { Spacing = 8 };
        if (error != null)
        {
            content.Children.Add(new TextBlock { Text = $"Error extracting ETW providers: {error}", TextWrapping = TextWrapping.Wrap });
        }
        else if (providers == null || providers.Count == 0)
        {
            content.Children.Add(new TextBlock { Text = "No ETW providers found in the selected binary." });
        }
        else
        {
            int authoritative = providers.Count(p => p.IsAuthoritative);
            content.Children.Add(new TextBlock
            {
                Text = $"{providers.Count} provider{(providers.Count == 1 ? "" : "s")} found · {authoritative} authoritative",
                FontWeight = FontWeights.SemiBold,
            });

            // Group by confidence so the trustworthy results sit above the guesses, each under a header.
            var auth = providers.Where(p => p.IsAuthoritative).ToList();
            var weak = providers.Where(p => !p.IsAuthoritative).ToList();
            if (auth.Count > 0 && weak.Count > 0)
            {
                content.Children.Add(ProviderGroup("Authoritative", auth));
                content.Children.Add(ProviderGroup("Lower confidence", weak));
            }
            else
            {
                foreach (var p in providers) content.Children.Add(ProviderRow(p));
            }

            if (providers.Any(p => p.Source == EtwProviderSource.Heuristic))
                content.Children.Add(new TextBlock
                {
                    Text = "No manifest/TraceLogging metadata was found; results above are low-confidence guesses.",
                    Foreground = new SolidColorBrush(Colors.OrangeRed),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0),
                });
        }
        dialog.Content = new ScrollViewer { Content = content, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        await dialog.ShowAsync();
    }

    // A labelled group of provider rows.
    private static FrameworkElement ProviderGroup(string header, System.Collections.Generic.IEnumerable<EtwProviderRef> items)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = header,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.Gray),
            Margin = new Thickness(0, 4, 0, 0),
        });
        foreach (var p in items) panel.Children.Add(ProviderRow(p));
        return panel;
    }

    // One provider: a confidence dot, the name (bold, wraps) over its GUID (monospace, selectable),
    // and a source chip on the right. Wrapping the name/GUID avoids the old horizontal clipping.
    private static FrameworkElement ProviderRow(EtwProviderRef p)
    {
        var color = ConfidenceColor(p.Source);
        var grid = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Border
        {
            Width = 10, Height = 10, CornerRadius = new CornerRadius(5),
            Background = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 0, 0),
        };
        Grid.SetColumn(dot, 0);

        var texts = new StackPanel { Spacing = 1 };
        texts.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(p.Name) ? "(name not in binary)" : p.Name,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });
        texts.Children.Add(new TextBlock
        {
            Text = p.Guid.ToString(),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.Gray),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        });
        Grid.SetColumn(texts, 1);

        var chip = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 1, 6, 2),
            VerticalAlignment = VerticalAlignment.Top,
            Background = new SolidColorBrush(Color.FromArgb(40, color.R, color.G, color.B)),
            Child = new TextBlock { Text = p.SourceLabel, FontSize = 11, Foreground = new SolidColorBrush(color) },
        };
        Grid.SetColumn(chip, 2);

        grid.Children.Add(dot);
        grid.Children.Add(texts);
        grid.Children.Add(chip);
        return grid;
    }

    private static Color ConfidenceColor(EtwProviderSource s) => s switch
    {
        EtwProviderSource.Manifest => Colors.LightGreen,
        EtwProviderSource.TraceLoggingVerified => Colors.MediumSeaGreen,
        EtwProviderSource.TraceLoggingDerived => Colors.Goldenrod,
        _ => Colors.OrangeRed,
    };

    private static TextBlock Bold(string text) => new() { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) };
}
