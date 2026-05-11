using System;
using System.Threading;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StreamingSearchHandle = FindNeedleUX.Services.MiddleLayerService.StreamingSearchHandle;

namespace FindNeedleUX.Pages;
public sealed partial class RunSearchPage : Page
{
    private CancellationTokenSource _cts;
    public RunSearchPage()
    {
        this.InitializeComponent();
    }

    private void SetControlsTo(bool enable)
    {
        busybar.ShowPaused = enable;
        busybar2.ShowPaused = enable;
        this.IsEnabled = enable;
        CancelButton.IsEnabled = !enable;
        if (!enable)
        {
            MainWindowActions.DisableNavBar();
        } else {
            MainWindowActions.EnableNavBar();
        }
    }

    private void GetNumberProgress(int count)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            busybar2.Value = count;
        });
    }

    private void GetTextProgress(string text)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            progresstext.Text = text;
        });
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        SetControlsTo(false);
        MiddleLayerService.GetProgressEventSink().RegisterForNumericProgress(GetNumberProgress);
        MiddleLayerService.GetProgressEventSink().RegisterForTextProgress(GetTextProgress);

        // Streaming path: SQLite is forced, search runs on threadpool, viewer can open while
        // it's still producing. We grant the search a short grace period — if it finishes in
        // that window we skip the auto-navigation to avoid flicker on trivial searches.
        StreamingSearchHandle handle = null;
        try
        {
            handle = MiddleLayerService.RunSearchStreaming(_shallowSearch);
            _cts = handle.Cancellation;

            const int GraceMs = 150;
            var grace = Task.Delay(GraceMs);
            var first = await Task.WhenAny(handle.SearchTask, grace);

            if (first != handle.SearchTask)
            {
                // Still running past the grace window — open the viewer now so the user sees
                // rows accumulating. The viewer subscribes to source.RowsAvailable and
                // refreshes on its own.
                MainWindowActions.NavigateToNativeResultsPage();
            }

            // Either way, keep showing progress here until the search finishes. Cancel button
            // is wired to handle.Cancellation via _cts.
            await handle.SearchTask;

            summary.Text = MiddleLayerService.GetStats()?.GetSummaryReport() ?? "";
        }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            summary.Text = "Search cancelled.";
        }
        catch (OperationCanceledException)
        {
            summary.Text = "Search cancelled.";
        }
        finally
        {
            SetControlsTo(true);
        }
    }

    private bool _shallowSearch;

    private void ShallowSearch_Click(object sender, RoutedEventArgs e) => _shallowSearch = true;
    private void NormalSearch_Click(object sender, RoutedEventArgs e) => _shallowSearch = false;
    private void DeepSearch_Click(object sender, RoutedEventArgs e) => _shallowSearch = false;

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        summary.Text = "Cancelling...";
    }
}
