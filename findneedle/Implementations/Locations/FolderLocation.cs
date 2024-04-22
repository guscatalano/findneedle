using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace findneedle.Implementations
{

    public class FolderLocation : SearchLocation
    {
        public string path
        {
            get; set;
        }

        [JsonConstructorAttribute]
        public FolderLocation(string path = "", int numRecordsInLastResult = 0, int numRecordsInMemory = 0, SearchLocationDepth depth = SearchLocationDepth.Intermediate)
        {
            this.path = path;
        }

        public override string GetDescription()
        {
            return "file/folder";   
        }
        public override string GetName()
        {
            return path;
        }

        public override void LoadInMemory(bool prefilter = false)
        {
            throw new NotImplementedException();
        }

        public override List<SearchResult> Search(SearchQuery searchQuery)
        {
            throw new NotImplementedException();
        }
    }
}
