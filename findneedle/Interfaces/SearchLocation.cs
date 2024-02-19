using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle
{
    //Defines how deep to search a given location. Deepest might imply pre-loading data that may not matter
    public enum SearchLocationDepth
    {
        Shallow = 0,
        Intermediate = 1,
        Deep = 2,
        Crush = 3 //Load everything
    }

    public abstract class SearchLocation
    {
        public int numRecordsInLastResult = 0;
        public int numRecordsInMemory = 0;
        private SearchLocationDepth depth = SearchLocationDepth.Intermediate;

        public abstract void LoadInMemory(bool prefilter= false);

        public abstract List<SearchResult> Search(SearchQuery searchQuery);

        public void SetSearchDepth(SearchLocationDepth depth)
        {
            this.depth = depth;
        }
        public SearchLocationDepth GetSearchDepth()
        {
            return this.depth;
        }

        public abstract string GetDescription();
        public abstract string GetName();

    }



}
