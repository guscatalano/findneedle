using System.Collections.Generic;
using FindNeedleUX.Services;
using FindNeedleUX.ViewObjects;

namespace FindNeedleUX.ViewModels;

/// <summary>
/// Production implementation of <see cref="ILocationStateService"/> — thin pass-through
/// to the <see cref="MiddleLayerService"/> statics. View-models depend on the interface;
/// tests pass a fake.
/// </summary>
public sealed class MiddleLayerLocationStateService : ILocationStateService
{
    public void AddFolderLocation(string path) => MiddleLayerService.AddFolderLocation(path);
    public void RemoveLocationByName(string name) => MiddleLayerService.RemoveLocationByName(name);
    public IReadOnlyList<LocationListItem> GetLocationListItems() => MiddleLayerService.GetLocationListItems();
}
