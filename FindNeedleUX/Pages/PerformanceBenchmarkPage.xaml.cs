using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FindPluginCore.Diagnostics.PerfBench;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Pages;

/// <summary>
/// Diagnostics → Performance benchmark. Runs <see cref="PerfBenchRunner"/> on THIS machine over
/// deterministic synthetic logs and shows the result, with an HTML report to read and a JSON file to
/// submit. The result contains only hardware specs + timings — no log data.
/// </summary>
public sealed partial class PerformanceBenchmarkPage : Page
{
    private PerfBenchResult _last;
    private string _htmlPath;
    private string _jsonPath;

    public PerformanceBenchmarkPage()
    {
        this.InitializeComponent();
        Loaded += (_, _) => ShowMachine();
    }

    private void ShowMachine()
    {
        try
        {
            var m = PerfBenchRunner.DescribeMachine();
            MachineText.Text = $"{m.CpuModel}  ·  {m.LogicalCores} cores  ·  {m.RamGB:0.#} GB  ·  {m.Os}";
        }
        catch { MachineText.Text = "(couldn't read machine specs)"; }
    }

    private async void Run_Click(object sender, RoutedEventArgs e)
    {
        long[] sizes = PresetCombo.SelectedIndex switch
        {
            2 => new long[] { 100_000, 1_000_000, 5_000_000, 10_000_000 },
            1 => new long[] { 100_000, 1_000_000, 5_000_000 },
            _ => new long[] { 100_000, 1_000_000 },
        };
        string preset = PresetCombo.SelectedIndex switch { 2 => "stress", 1 => "full", _ => "quick" };

        RunButton.IsEnabled = false;
        Progress.IsActive = true;
        ResultsCard.Visibility = Visibility.Collapsed;
        StatusText.Text = "Running… this uses the CPU/disk for a bit.";

        PerfBenchResult res = null;
        try { res = await Task.Run(() => PerfBenchRunner.Run(sizes, repeats: 3, preset: preset)); }
        catch (Exception ex) { StatusText.Text = "Failed: " + ex.Message; }
        finally { RunButton.IsEnabled = true; Progress.IsActive = false; }
        if (res == null) return;

        _last = res;
        StatusText.Text = $"Done in {res.DurationOfRunSec:0.#}s.";
        WriteArtifacts(res);
        RenderResults(res);
        ResultsCard.Visibility = Visibility.Visible;
    }

    private async void Profile_Click(object sender, RoutedEventArgs e)
    {
        RunButton.IsEnabled = false;
        ProfileButton.IsEnabled = false;
        Progress.IsActive = true;
        ResultsCard.Visibility = Visibility.Collapsed;
        StatusText.Text = "Profiling a 1M-line load+search… this samples the CPU and takes a bit longer.";

        PerfBenchResult res = null;
        try { res = await Task.Run(() => PerfBenchRunner.RunProfile(1_000_000)); }
        catch (Exception ex) { StatusText.Text = "Failed: " + ex.Message; }
        finally { RunButton.IsEnabled = true; ProfileButton.IsEnabled = true; Progress.IsActive = false; }
        if (res == null) return;

        _last = res;
        StatusText.Text = $"Profiled in {res.DurationOfRunSec:0.#}s. Open the report to see the hot code paths.";
        WriteArtifacts(res);
        RenderResults(res);
        ResultsCard.Visibility = Visibility.Visible;
    }

    private void RenderResults(PerfBenchResult r)
    {
        ResultsHost.Children.Clear();

        if (r.HotMethods.Count > 0)
        {
            ResultsHost.Children.Add(new TextBlock
            {
                Text = "Hottest code paths (share of CPU time):",
                FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            });
            foreach (var f in r.HotMethods)
                ResultsHost.Children.Add(new TextBlock
                {
                    Text = $"{f.Percent,5:0.#}%   {f.Method}",
                    FontSize = 13, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    TextWrapping = TextWrapping.Wrap,
                });
            return;
        }

        foreach (var s in r.Scenarios)
        {
            var sb = new StringBuilder();
            sb.Append(s.Id).Append("  —  ").Append(s.Rows.ToString("N0")).Append(" rows");
            foreach (var kv in s.Ratios)
                sb.Append("   ·   ").Append(kv.Key).Append(' ').Append(kv.Value);
            if (s.Cold != null)
            {
                if (s.Cold.TryGetValue("ingestMs", out var ing)) sb.Append("   ·   ingest ").Append(ing).Append(" ms");
                if (s.Cold.TryGetValue("indexBuildMs", out var idx)) sb.Append("   ·   index ").Append(idx).Append(" ms");
            }
            ResultsHost.Children.Add(new TextBlock { Text = sb.ToString(), FontSize = 13, TextWrapping = TextWrapping.Wrap });
        }
    }

    private void WriteArtifacts(PerfBenchResult r)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                   "FindNeedle", "perfbench");
            Directory.CreateDirectory(dir);
            _htmlPath = Path.Combine(dir, $"findneedle-perfbench-{r.RunId}.html");
            _jsonPath = Path.Combine(dir, $"findneedle-perfbench-{r.RunId}.json");
            PerfBenchReport.WriteHtml(r, _htmlPath);
            PerfBenchReport.WriteJson(r, _jsonPath);
        }
        catch (Exception ex) { StatusText.Text += "  (couldn't write report: " + ex.Message + ")"; }
    }

    private void OpenHtml_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_htmlPath) || !File.Exists(_htmlPath)) return;
        try { Process.Start(new ProcessStartInfo { FileName = _htmlPath, UseShellExecute = true }); } catch { }
    }

    private void ShowJson_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_jsonPath) || !File.Exists(_jsonPath)) return;
        try { Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{_jsonPath}\"", UseShellExecute = true }); } catch { }
    }
}
