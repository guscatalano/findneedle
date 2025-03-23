using findneedle;
using findneedle.Interfaces;
using FindNeedlePluginLib.Interfaces;

namespace TestProcessorPlugin;

public class SampleProcessStatistics : IResultProcessor, IPluginDescription
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

    public string GetDescription() {
        return "This is a sample plugin that just counts how many results there are";
    }

    public void ProcessResults(List<ISearchResult> results)
    {
        countResults = results.Count;
    }
    public string GetTextDescription()
    {
        return "";
    }
}
