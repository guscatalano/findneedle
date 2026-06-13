using System.Collections.Generic;
using FindNeedleUX.ViewObjects;

namespace FindNeedleUX.ViewModels;

/// <summary>
/// Abstraction over <see cref="FindNeedleUX.Services.MiddleLayerService"/>'s static
/// location surface, so <see cref="SearchLocationsViewModel"/> can be exercised in
/// unit tests without the static <c>Locations</c> list or the plugin manager.
/// Mirrors the role <see cref="IQueryStateService"/> plays for the rules page.
/// </summary>
public interface ILocationStateService
{
    /// <summary>
    /// Adds a folder/file path as a search location. Mirrors
    /// <c>MiddleLayerService.AddFolderLocation(string)</c>.
    /// </summary>
    void AddFolderLocation(string path);

    /// <summary>
    /// Removes the location whose display name matches <paramref name="name"/>.
    /// Mirrors <c>MiddleLayerService.RemoveLocationByName(string)</c>.
    /// </summary>
    void RemoveLocationByName(string name);

    /// <summary>
    /// Returns the current locations as display items. Mirrors
    /// <c>MiddleLayerService.GetLocationListItems()</c>.
    /// </summary>
    IReadOnlyList<LocationListItem> GetLocationListItems();
}
