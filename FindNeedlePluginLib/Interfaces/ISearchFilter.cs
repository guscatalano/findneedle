using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle;

public interface ISearchFilter
{
    public abstract bool Filter(ISearchResult entry);
    public abstract string GetDescription();
    public abstract string GetName();

 

}
