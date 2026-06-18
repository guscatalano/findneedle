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
        stack.Children.Add(new TextBlock { Text = "Select a binary file to inspect for ETW providers." });
        warning.Content = stack;
        if (await warning.ShowAsync() != ContentDialogResult.Primary) return;

        showSpinner(true, "Inspecting binary file...");
        var hWnd = WindowNative.GetWindowHandle(window);
        var path = Win32FileDialog.OpenFile(hWnd, new (string, string)[] { ("Binary files", "*.exe;*.dll"), ("All files", "*.*") });
        if (path == null) { showSpinner(false, null); return; }

        List<(Guid guid, string? name)> providers = null!;
        string error = null!;
        await Task.Run(() =>
        {
            try { providers = EtwNativeProviderScanner.ExtractNativeEtwProviders(path); }
            catch (Exception ex) { error = ex.Message; }
        });
        showSpinner(false, null);

        var dialog = new ContentDialog
        {
            Title = error == null ? "ETW Providers in Binary" : "ETW Provider Extraction Error",
            CloseButtonText = "OK",
            MinWidth = 600,
            XamlRoot = window.Content.XamlRoot
        };
        var content = new StackPanel();
        if (error != null)
        {
            content.Children.Add(new TextBlock { Text = $"Error extracting ETW providers: {error}" });
        }
        else if (providers == null || providers.Count == 0)
        {
            content.Children.Add(new TextBlock { Text = "No ETW providers found in the selected binary." });
        }
        else
        {
            content.Children.Add(Bold($"ETW Providers found ({providers.Count}):"));
            foreach (var p in providers)
            {
                var line = p.name != null ? $"{p.guid}  |  {p.name}" : p.guid.ToString();
                content.Children.Add(new TextBlock { Text = line, FontFamily = new FontFamily("Consolas") });
            }
            var testButton = new Button { Content = "Test Collection", Margin = new Thickness(0, 12, 0, 0) };
            testButton.Click += async (s, e) =>
            {
                dialog.Hide();
                await TestEtwCollectionAsync(window, showSpinner, providers.Select(x => x.name ?? x.guid.ToString()).ToList());
            };
            content.Children.Add(testButton);
        }
        dialog.Content = content;
        await dialog.ShowAsync();
    }

    private static async Task TestEtwCollectionAsync(Window window, Action<bool, string> showSpinner, List<string> providers)
    {
        showSpinner(true, "Testing ETW collection...");
        var found = new List<(string provider, int count)>();
        foreach (var provider in providers)
        {
            try
            {
                var collector = new ETWPlugin.Locations.LiveCollector();
                collector.Setup(new List<string> { provider }, TimeSpan.FromSeconds(2), 0);
                collector.StartCollecting();
                await Task.Delay(2200);
                collector.StopCollecting();
                found.Add((provider, collector.GetResultsInMemory().Count));
            }
            catch { found.Add((provider, 0)); }
        }
        showSpinner(false, null);

        var dialog = new ContentDialog
        {
            Title = "Test Collection Results",
            CloseButtonText = "OK",
            PrimaryButtonText = "Copy List",
            MinWidth = 600,
            XamlRoot = window.Content.XamlRoot
        };
        var content = new StackPanel();
        if (found.Count == 0)
        {
            content.Children.Add(new TextBlock { Text = "No providers were tested." });
        }
        else
        {
            content.Children.Add(Bold("Providers tested (event count may include false positives):"));
            foreach (var p in found)
                content.Children.Add(new TextBlock { Text = $"{p.provider} ({p.count})", FontFamily = new FontFamily("Consolas") });
        }
        dialog.Content = content;
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && found.Count > 0)
        {
            var pkg = new DataPackage();
            pkg.SetText(string.Join("\n", found.Select(x => x.provider)));
            Clipboard.SetContent(pkg);
        }
    }

    private static TextBlock Bold(string text) => new() { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) };

    private static void AppendList(StackPanel stack, string header, IList<string> items, int max)
    {
        stack.Children.Add(Bold(header));
        foreach (var p in items.Take(max)) stack.Children.Add(new TextBlock { Text = p });
        if (items.Count > max) stack.Children.Add(new TextBlock { Text = $"...and {items.Count - max} more" });
    }
}
