using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle
{
    public interface SearchFilter
    {
        public bool Filter(SearchResult entry);
        public abstract string GetDescription();
        public abstract string GetName();

    }


}
