using findneedle.Interfaces;

namespace findneedle.Implementations.ResultProcessors;
public class WatsonCrashProcessor : ResultProcessor
{


    public string GetOutputFile(string optionalOutputFolder = "")
    {
        return "";
    }

    public void ProcessResults(List<SearchResult> results)
    {
        List<SearchResult> resultList = new List<SearchResult>();
        foreach (SearchResult result in results)
        {
            if (result.GetSearchableData().Contains("A .NET application failed."))
            {
                resultList.Add(result);
            }

            if (result.GetSearchableData().Contains("Application Hang"))
            {
                resultList.Add(result);
            }
        }
    }
}
