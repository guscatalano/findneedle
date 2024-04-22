using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Interfaces
{
    interface SearchOutput
    {
        public void WriteAllOutput(List<SearchResult> result);
        public void WriteOutput(SearchResult result);
        public string GetOutputFileName();
    }
}
