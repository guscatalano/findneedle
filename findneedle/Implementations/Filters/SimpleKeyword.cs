using System.Text.Json.Serialization;

namespace findneedle.Implementations
{
    public class SimpleKeywordFilter : SearchFilter
    {

        public string term 
        {
            get; set;
        }

        [JsonConstructorAttribute]
        public SimpleKeywordFilter(string term)
        {
            this.term = term.Trim();
            if (string.IsNullOrEmpty(term))
            {
                throw new Exception("Can't search for empty terms");
            }
        }



        public bool Filter(SearchResult entry)
        {
            if (entry.GetSearchableData().ToLower().Contains(term))
            {
                return true;
            }
            return false;
        }

        public string GetDescription()
        {
            return "SimpleKeyword";
        }
        public string GetName()
        {
            return term;
        }


    }

}
