using System.Collections.Generic;

namespace FindNeedleUX.ViewObjects;
public class FilterSource
{
    private List<FilterListItem> _filters;

    public FilterSource()
    {
        _filters = new List<FilterListItem>();
    }
}
