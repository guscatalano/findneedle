namespace findneedle.Interfaces;
public interface ResultProcessor
{
    public void ProcessResults(List<SearchResult> results);
    public string GetOutputFile(string optionalOutputFolder = "");
}
