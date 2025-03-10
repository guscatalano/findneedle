using findneedle;
using findneedle.Interfaces;

namespace TestProcessorPlugin;

public class SampleZeroStatistics : IResultProcessor
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
        return "This is a sample plugin that always 'counts' 0 results";
    }

    public void ProcessResults(List<ISearchResult> results)
    {
        countResults = 0;
    }
}
