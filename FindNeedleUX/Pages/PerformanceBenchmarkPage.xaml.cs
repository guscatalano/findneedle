using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.WinUI.UI.Controls;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using FindNeedleUX.Pages.NativeResultViewer;
using FindNeedleUX.Services.PagedLogSource;
using FindPluginCore.Diagnostics.PerfBench;
using FindPluginCore.Implementations.Storage;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;

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

    // ===== Viewer responsiveness (UI-thread render measurement) =====

    private async void Viewer_Click(object sender, RoutedEventArgs e)
    {
        const long rows = 200_000;
        SetBusy(true, $"Building a {rows:N0}-line log for the viewer test…");

        string dbBase = Path.Combine(Path.GetTempPath(), "perfbench_viewer_" + Guid.NewGuid().ToString("N"));
        SqliteStorage storage = null;
        DataGrid grid = null;
        try
        {
            // Generate + ingest + index OFF the UI thread; only the render is measured on the UI thread.
            bool priorFts = SqliteStorage.DisableFtsForMeasurement;
            storage = await Task.Run(() =>
            {
                SqliteStorage.DisableFtsForMeasurement = false;
                var s = new SqliteStorage(dbBase);
                s.AddFilteredBatch(SynthRows(rows));
                s.BuildSearchIndex();
                return s;
            });
            SqliteStorage.DisableFtsForMeasurement = priorFts;

            StatusText.Text = "Measuring viewer render…";
            var source = new SqlitePagedSource(storage, ownsStorage: false);
            var vm = new NativeResultsPageViewModel();
            vm.SetSourceForTests(source);
            vm.PageSize = 100;
            vm.TotalCount = source.TotalCount;

            grid = BuildBenchGrid();
            grid.ItemsSource = vm.Results;
            ViewerBenchHost.Child = grid;
            ViewerBenchHost.Visibility = Visibility.Visible;

            // Warm up (also establishes TotalFilteredCount / TotalPages, which LastPage needs).
            await MeasureRenderAsync(vm, grid, () => _ = vm.ApplyFiltersAsync(CancellationToken.None));
            await SettleAsync();

            double firstPage = await MedianRenderAsync(3, vm, grid,
                measure: () => _ = vm.ApplyFiltersAsync(CancellationToken.None));           // re-render page 1

            vm.FirstPage(); await SettleAsync();
            double pageFwd = await MedianRenderAsync(5, vm, grid, measure: () => vm.NextPage()); // advance a page each time

            double jumpLast = await MedianRenderAsync(3, vm, grid,
                reset: () => vm.FirstPage(), measure: () => vm.LastPage());                  // jump to the end

            string term = SyntheticLogGenerator.RareTokenPrefix + "1";
            double filter = await MedianRenderAsync(3, vm, grid,
                reset: () => vm.SearchText = "", measure: () => vm.SearchText = term);       // filter as typed

            var scenario = new PerfBenchScenario
            {
                Id = "viewer.text." + (rows >= 1_000_000 ? $"{rows / 1_000_000}M" : $"{rows / 1000}k"),
                Kind = "viewer", Dataset = "synthetic-log", DatasetVersion = SyntheticLogGenerator.DatasetVersion,
                Rows = rows, StorageTierChosen = "SQLite",
                Metrics = new()
                {
                    ["firstPageMs"] = Math.Round(firstPage, 1),
                    ["pageForwardMs"] = Math.Round(pageFwd, 1),
                    ["jumpToLastMs"] = Math.Round(jumpLast, 1),
                    ["filterApplyMs"] = Math.Round(filter, 1),
                },
            };
            var res = new PerfBenchResult
            {
                RunId = Guid.NewGuid().ToString("N").Substring(0, 12),
                TimestampUtc = DateTime.UtcNow.ToString("o"),
                Preset = "viewer", Repeats = 3,
                Machine = PerfBenchRunner.DescribeMachine(),
                Scenarios = { scenario },
            };
            try { res.SystemLoad.AvailableRamGB = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1e9, 1); } catch { }

            _last = res;
            StatusText.Text = "Viewer measured. Open the report for the on-screen render times.";
            WriteArtifacts(res);
            RenderResults(res);
            ResultsCard.Visibility = Visibility.Visible;
        }
        catch (Exception ex) { StatusText.Text = "Viewer measurement failed: " + ex.Message; }
        finally
        {
            if (grid != null) grid.ItemsSource = null;
            ViewerBenchHost.Child = null;
            ViewerBenchHost.Visibility = Visibility.Collapsed;
            try { storage?.Dispose(); } catch { }
            TryDeleteDb(dbBase);
            SetBusy(false, null);
        }
    }

    /// <summary>Median of N render measurements. <paramref name="reset"/> (optional) runs + settles before
    /// each measured trigger, so the measured op always causes a fresh page render.</summary>
    private async Task<double> MedianRenderAsync(int n, NativeResultsPageViewModel vm, DataGrid grid,
        Action measure, Action reset = null)
    {
        var xs = new List<double>();
        for (int i = 0; i < n; i++)
        {
            if (reset != null) { reset(); await SettleAsync(); }
            xs.Add(await MeasureRenderAsync(vm, grid, measure));
        }
        xs.Sort();
        return xs.Count == 0 ? 0 : xs[xs.Count / 2];
    }

    /// <summary>Time from an action to the rows actually painted: fire the trigger, wait for the page swap
    /// (Results Reset), then stop on the grid's next LayoutUpdated (rows arranged). A dispatcher backstop
    /// guarantees completion if no render happens.</summary>
    private async Task<double> MeasureRenderAsync(NativeResultsPageViewModel vm, DataGrid grid, Action trigger)
    {
        var tcs = new TaskCompletionSource<double>();
        var sw = new Stopwatch();
        bool done = false;
        NotifyCollectionChangedEventHandler onCol = null;
        EventHandler<object> onLayout = null;

        void Finish(double ms)
        {
            if (done) return;
            done = true;
            try { vm.Results.CollectionChanged -= onCol; } catch { }
            try { grid.LayoutUpdated -= onLayout; } catch { }
            tcs.TrySetResult(ms);
        }
        onLayout = (_, __) => { sw.Stop(); Finish(sw.Elapsed.TotalMilliseconds); };
        onCol = (_, a) =>
        {
            if (a.Action != NotifyCollectionChangedAction.Reset) return;
            try { vm.Results.CollectionChanged -= onCol; } catch { }
            grid.LayoutUpdated += onLayout; // stop on the arrange that follows the swap
        };

        vm.Results.CollectionChanged += onCol;
        sw.Start();
        trigger();

        // Backstop: if nothing rendered within 5 s, record the elapsed and move on.
        _ = Task.Delay(5000).ContinueWith(_ =>
            DispatcherQueue.TryEnqueue(() => { if (!done) { sw.Stop(); Finish(sw.Elapsed.TotalMilliseconds); } }));

        return await tcs.Task;
    }

    /// <summary>Await one UI-thread idle turn (Low-priority drain) plus a short beat.</summary>
    private async Task SettleAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => tcs.TrySetResult(true)))
            tcs.TrySetResult(true);
        await tcs.Task;
        await Task.Delay(60);
    }

    /// <summary>A CommunityToolkit DataGrid mirroring the real results grid's columns/widths/row height, so
    /// the layout + paint cost measured here matches the real viewer.</summary>
    private static DataGrid BuildBenchGrid()
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false, IsReadOnly = true, RowHeight = 26,
            HeadersVisibility = DataGridHeadersVisibility.All, GridLinesVisibility = DataGridGridLinesVisibility.None,
        };
        void Col(string header, string path, double width)
            => grid.Columns.Add(new DataGridTextColumn
            {
                Header = header, Width = new DataGridLength(width),
                Binding = new Binding { Path = new PropertyPath(path) },
            });
        Col("Index", "Index", 70); Col("Time", "LogTime", 160); Col("Provider", "Provider", 120);
        Col("TaskName", "TaskName", 140); Col("Message", "Message", 800); Col("Source", "Source", 140);
        Col("Level", "Level", 90); Col("ProcessId", "ProcessId", 80); Col("ProcessName", "ProcessName", 140);
        Col("ThreadId", "ThreadId", 80); Col("ActivityId", "ActivityId", 240); Col("EventId", "EventId", 80);
        Col("OpCode", "OpCode", 100); Col("Keywords", "Keywords", 140); Col("RelatedActivityId", "RelatedActivityId", 240);
        Col("Channel", "Channel", 120); Col("ProviderGuid", "ProviderGuid", 240); Col("RecordId", "RecordId", 90);
        Col("Raw Row", "SearchableData", 500);
        return grid;
    }

    private void SetBusy(bool busy, string status)
    {
        RunButton.IsEnabled = ProfileButton.IsEnabled = ViewerButton.IsEnabled = !busy;
        Progress.IsActive = busy;
        if (status != null) StatusText.Text = status;
        if (busy) ResultsCard.Visibility = Visibility.Collapsed;
    }

    private static IEnumerable<ISearchResult> SynthRows(long n)
    {
        for (long i = 0; i < n; i++) yield return new BenchRow(i);
    }

    /// <summary>Synthetic row for the viewer harness — deterministic message/time from the generator, with a
    /// rotating level so the grid renders varied content.</summary>
    private sealed class BenchRow : ISearchResult
    {
        private readonly long _i;
        public BenchRow(long i) { _i = i; }
        public DateTime GetLogTime() => SyntheticLogGenerator.Time(_i);
        public string GetMachineName() => "bench";
        public void WriteToConsole() { }
        public Level GetLevel() => (Level)(int)(_i % 3);
        public string GetUsername() => "u";
        public string GetTaskName() => "task";
        public string GetOpCode() => "";
        public string GetSource() => "synthetic";
        public string GetSearchableData() => SyntheticLogGenerator.Message(_i);
        public string GetMessage() => SyntheticLogGenerator.Message(_i);
        public string GetResultSource() => "perfbench";
    }

    private static void TryDeleteDb(string dbBase)
    {
        try
        {
            var db = FindNeedleCoreUtils.CachedStorage.GetCacheFilePath(dbBase, ".db");
            foreach (var f in new[] { db, db + "-wal", db + "-shm", db + "-journal" })
                try { if (File.Exists(f)) File.Delete(f); } catch { }
        }
        catch { }
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
            if (s.Kind == "viewer")
            {
                foreach (var kv in new[] { ("firstPageMs", "first page"), ("pageForwardMs", "scroll"), ("jumpToLastMs", "jump to end"), ("filterApplyMs", "filter") })
                    if (s.Metrics.TryGetValue(kv.Item1, out var v)) sb.Append("   ·   ").Append(kv.Item2).Append(' ').Append(v).Append(" ms");
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
