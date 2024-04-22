namespace findneedle
{
    public interface SearchFilter
    {
        public abstract bool Filter(SearchResult entry);
        public abstract string GetDescription();
        public abstract string GetName();



    }


}
