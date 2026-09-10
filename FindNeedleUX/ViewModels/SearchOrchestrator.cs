using System;
using System.Threading;
using System.Threading.Tasks;

namespace FindNeedleUX.ViewModels;

/// <summary>
/// Testable run/cancel/status orchestration for <see cref="FindNeedleUX.Pages.RunSearchPage"/>.
/// Owns the streaming-search lifecycle: kick off the search, open the viewer if it runs past a
/// short grace window (so trivial searches don't flicker the viewer open), await completion, and
/// surface a status string. Has no WinUI dependency — the page passes callbacks for the two UI
/// side effects (open viewer, update status text) and wires Cancel to a button.
/// </summary>
public sealed class SearchOrchestrator
{
    private readonly ISearchRunner _runner;
    private CancellationTokenSource? _cts;

    /// <summary>Default grace window before auto-opening the viewer for a still-running search.</summary>
    public const int DefaultGraceMs = 150;

    public SearchOrchestrator() : this(new MiddleLayerSearchRunner()) { }

    public SearchOrchestrator(ISearchRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// Runs a streaming search to completion. <paramref name="onOpenViewer"/> fires only if the
    /// search is still running after <paramref name="graceMs"/>; <paramref name="onStatus"/> is
    /// called once at the end with the result summary (or "Search cancelled." if cancelled).
    /// Never throws for the cancel case — that's reported through <paramref name="onStatus"/>.
    /// </summary>
    public async Task RunAsync(
        bool shallowSearch,
        Action onOpenViewer,
        Action<string> onStatus,
        int graceMs = DefaultGraceMs)
    {
        try
        {
            var handle = _runner.RunStreaming(shallowSearch);
            _cts = handle.Cancellation;

            var grace = Task.Delay(graceMs);
            var first = await Task.WhenAny(handle.SearchTask, grace);
            if (first != handle.SearchTask)
            {
                // Still running past the grace window — open the viewer so the user sees rows
                // accumulate. (A fast search finishes within the window and skips this.)
                onOpenViewer();
            }

            await handle.SearchTask;

            var report = _runner.GetSummaryReport();
            onStatus(_runner.LastSearchReusedCache ? "(from cache) " + report : "(scanned) " + report);
        }
        catch (TaskCanceledException)
        {
            onStatus("Search cancelled.");
        }
        catch (OperationCanceledException)
        {
            onStatus("Search cancelled.");
        }
        catch (Exception ex)
        {
            // BACKSTOP: a failed search must never take the process down. Callers of RunAsync are
            // async void UI handlers (RunSearchPage.Button_Click and friends), so anything escaping
            // here is reposted to the UI thread by the runtime and the rethrow trips a WinUI
            // RaiseFailFastException (0xC000027B) — a kill App.UnhandledException cannot intercept,
            // leaving no managed stack and nothing in the app log. That is exactly how one unreadable
            // .evtx under C:\Windows\System32\winevt\Logs killed the app.
            //
            // Individual causes still get fixed at their source (see FolderLocation), but a search
            // that fails for ANY reason should end as a message, not a vanished window.
            var reason = Unwrap(ex);
            FindNeedlePluginLib.Logger.Instance.Log($"Search failed: {reason.GetType().Name}: {reason.Message}\n{ex}");
            onStatus($"Search failed: {reason.Message}");
        }
    }

    /// <summary>The exception worth showing: task plumbing wraps the real fault in AggregateException.</summary>
    private static Exception Unwrap(Exception ex)
        => ex is AggregateException { InnerException: { } inner } ? Unwrap(inner) : ex;

    /// <summary>Requests cancellation of the in-flight search, if any.</summary>
    public void Cancel() => _cts?.Cancel();
}
