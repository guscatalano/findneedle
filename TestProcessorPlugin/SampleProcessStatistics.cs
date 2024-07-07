using findneedle;
using findneedle.Interfaces;

namespace TestProcessorPlugin;

public class SampleProcessStatistics : ResultProcessor
{


    int countResults = 0;

    public string GetClassName()
    {
        Type me = this.GetType();
        if (me.FullName == null)
        {
            throw new Exception("Fullname was null???");
        }
        else
        {
            return me.FullName;
        }
    }

    public string GetFriendlyName() {
        return "SampleProcessStatistics"; 
    }

    public string GetOutputFile(string optionalOutputFolder = "") {
        return "";
    }

    public string GetOutputText()
    {
        return "There were: " + countResults + " results.";
    }

    public string GetTextDescription() {
        return "This is a sample plugin that just counts how many results there are";
    }

    public void ProcessResults(List<SearchResult> results)
    {
        countResults = results.Count;
    }
}
