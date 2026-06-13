using System.Collections.ObjectModel;
using FindNeedleUX.ViewObjects;

namespace FindNeedleUX.ViewModels;

/// <summary>
/// Testable logic for <see cref="FindNeedleUX.Pages.SearchLocationsPage"/> — adding,
/// removing, and listing search locations. Has no WinUI dependencies and can be
/// exercised without a window.
///
/// The page constructs one of these, binds its repeater to <see cref="Locations"/>
/// once, and forwards its Add/Remove button handlers (after running the file/folder
/// picker, which stays in the page because it needs a window handle) into the public
/// methods here. <see cref="Locations"/> is mutated in place so the bound ItemsSource
/// stays stable across refreshes.
/// </summary>
public sealed class SearchLocationsViewModel
{
    private readonly ILocationStateService _locationState;

    /// <summary>
    /// The current search locations as display items. A stable collection — refreshed
    /// in place after every Add/Remove so existing bindings keep working.
    /// </summary>
    public ObservableCollection<LocationListItem> Locations { get; } = new();

    public SearchLocationsViewModel() : this(new MiddleLayerLocationStateService()) { }

    public SearchLocationsViewModel(ILocationStateService locationState)
    {
        _locationState = locationState;
        Refresh();
    }

    /// <summary>
    /// Adds a folder or file path as a search location, then refreshes the list.
    /// (Files are added through the same path as folders — see the page handlers.)
    /// </summary>
    public void AddLocation(string path)
    {
        _locationState.AddFolderLocation(path);
        Refresh();
    }

    /// <summary>
    /// Removes the location with the given display name, then refreshes the list.
    /// No-op if no location matches.
    /// </summary>
    public void RemoveLocation(string name)
    {
        _locationState.RemoveLocationByName(name);
        Refresh();
    }

    /// <summary>
    /// Rebuilds <see cref="Locations"/> in place from the authoritative state service.
    /// </summary>
    public void Refresh()
    {
        Locations.Clear();
        foreach (var item in _locationState.GetLocationListItems())
            Locations.Add(item);
    }
}
