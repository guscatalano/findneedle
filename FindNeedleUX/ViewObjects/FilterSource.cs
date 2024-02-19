using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedleUX.ViewObjects;
public class FilterSource
{
    private List<FilterListItem> _filters;

    public FilterSource()
    {
        _filters = new List<FilterListItem>();
    }
}
