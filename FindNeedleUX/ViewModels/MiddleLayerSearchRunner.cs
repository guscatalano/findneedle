using FindNeedleUX.Services;

namespace FindNeedleUX.ViewModels;

/// <summary>
/// Production <see cref="ISearchRunner"/> — thin pass-through to the <see cref="MiddleLayerService"/>
/// streaming-search statics. The orchestrator depends on the interface; tests pass a fake.
/// </summary>
public sealed class MiddleLayerSearchRunner : ISearchRunner
{
    public SearchRunHandle RunStreaming(bool shallowSearch)
    {
        var handle = MiddleLayerService.RunSearchStreaming(shallowSearch);
        return new SearchRunHandle
        {
            SearchTask = handle.SearchTask,
            Cancellation = handle.Cancellation,
        };
    }

    public string GetSummaryReport() => MiddleLayerService.GetStats()?.GetSummaryReport() ?? string.Empty;

    public bool LastSearchReusedCache => MiddleLayerService.LastSearchReusedCache;
}
