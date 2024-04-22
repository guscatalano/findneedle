using findneedle.Interfaces;

namespace findneedle.Implementations.Outputs
{
    public class NullOutput : SearchOutput
    {
        public string GetOutputFileName()
        {
            return "(the void)";
        }

        public void WriteAllOutput(List<SearchResult> result)
        {
            //do nothing
        }

        public void WriteOutput(SearchResult result)
        {
            //do nothing
        }
    }
}
