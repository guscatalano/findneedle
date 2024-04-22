namespace findneedle.Interfaces
{
    interface SearchOutput
    {
        public void WriteAllOutput(List<SearchResult> result);
        public void WriteOutput(SearchResult result);
        public string GetOutputFileName();
    }
}
