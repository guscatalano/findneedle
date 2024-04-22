namespace findneedle.Implementations
{
    public class TimeRangeFilter : SearchFilter
    {
        private DateTime start;
        private DateTime end;
        public TimeRangeFilter(DateTime start, DateTime end)
        {
            this.start = start;
            this.end = end;
        }

        public string SearchFilterType => throw new NotImplementedException();

        public bool Filter(SearchResult entry)
        {
            return true;
        }

        public string GetDescription()
        {
            return "TimeRange";
        }
        public string GetName()
        {
            return ":(";
        }

        public string GetSerializedJson() => throw new NotImplementedException();
    }
}
