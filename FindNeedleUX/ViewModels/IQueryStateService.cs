using FindNeedlePluginLib;

namespace FindNeedleUX.ViewModels;

/// <summary>
/// Abstraction over <see cref="FindNeedleUX.Services.MiddleLayerService"/>'s static
/// query-state surface, so view-models can be exercised in unit tests without
/// instantiating the static SearchQueryUX or wiring the StateChanged event.
/// </summary>
public interface IQueryStateService
{
    /// <summary>
    /// Returns the current search query, or null if none is set. Mirrors
    /// <c>MiddleLayerService.GetCurrentQuery()</c>.
    /// </summary>
    ISearchQuery? GetCurrentQuery();

    /// <summary>
    /// Notifies subscribers that query-shaped state changed. Mirrors
    /// <c>MiddleLayerService.NotifyStateChanged()</c>.
    /// </summary>
    void NotifyStateChanged();
}
