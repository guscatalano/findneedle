namespace FindNeedleUX.ViewObjects;
public class LocationListItem
{
    public string Name
    {
        get; set;
    }

    public string Description
    {
        get; set;
    }

    /// <summary>True for locations with editable settings (e.g. Kusto: cluster/db/query/auth).</summary>
    public bool IsEditable
    {
        get; set;
    }

    /// <summary>Visibility for the row's Edit button (shown only when <see cref="IsEditable"/>).</summary>
    public Microsoft.UI.Xaml.Visibility EditVisibility
        => IsEditable ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
}
