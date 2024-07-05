using findneedle;
using findneedle.Interfaces;

namespace TestProcessorPlugin;

public class SampleProcessStatistics : ResultProcessor
{
    int countResults = 0;
    public string GetOutputFile(string optionalOutputFolder = "") {
        return "";
    }

    public string GetOutputText()
    {
        return "There were: " + countResults + " results.";
    }
    public void ProcessResults(List<SearchResult> results)
    {
        countResults = results.Count;
    }
}
