using FindNeedlePluginLib;
using FindNeedleUX.Services;

namespace FindNeedleUX.ViewModels;

/// <summary>
/// Production implementation of <see cref="IQueryStateService"/> — thin pass-through
/// to the <see cref="MiddleLayerService"/> statics. View-models depend on the interface;
/// tests pass a fake.
/// </summary>
public sealed class MiddleLayerQueryStateService : IQueryStateService
{
    public ISearchQuery? GetCurrentQuery() => MiddleLayerService.GetCurrentQuery();
    public void NotifyStateChanged() => MiddleLayerService.NotifyStateChanged();
}
