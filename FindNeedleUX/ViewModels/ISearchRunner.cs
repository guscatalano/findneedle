using System.Threading;
using System.Threading.Tasks;

namespace FindNeedleUX.ViewModels;

/// <summary>
/// A running streaming search: the background task plus the handle used to cancel it.
/// Mirrors the fields of <c>MiddleLayerService.StreamingSearchHandle</c> that the orchestrator
/// actually needs, so <see cref="SearchOrchestrator"/> can be tested without the WinUI services.
/// </summary>
public sealed class SearchRunHandle
{
    public Task SearchTask { get; init; } = Task.CompletedTask;
    public CancellationTokenSource Cancellation { get; init; } = new();
}

/// <summary>
/// Abstraction over the streaming-search surface of <c>MiddleLayerService</c>, so the run/cancel/
/// progress orchestration in <see cref="SearchOrchestrator"/> has no WinUI dependency and can be
/// unit-tested. Mirrors <see cref="IQueryStateService"/>'s role for the rules page.
/// </summary>
public interface ISearchRunner
{
    /// <summary>Starts a streaming search and returns its task + cancel handle.</summary>
    SearchRunHandle RunStreaming(bool shallowSearch);

    /// <summary>Summary text for the just-completed search (empty if none).</summary>
    string GetSummaryReport();

    /// <summary>True if the just-completed search reused the on-disk cache instead of scanning.</summary>
    bool LastSearchReusedCache { get; }
}
