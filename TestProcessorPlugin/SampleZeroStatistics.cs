using System.Diagnostics.CodeAnalysis;
using findneedle;
using findneedle.Interfaces;
using FindNeedlePluginLib.Interfaces;

namespace TestProcessorPlugin;

[ExcludeFromCodeCoverage]
public class SampleZeroStatistics : IResultProcessor, IPluginDescription
{


    int countResults = 0;

    public string GetPluginClassName()
    {
        return IPluginDescription.GetPluginClassNameBase(this);
    }

    public string GetPluginFriendlyName() {
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
        return "This is a sample plugin that always 'counts' 0 results";
    }

    public void ProcessResults(List<ISearchResult> results)
    {
        countResults = 0;
    }

    public string GetPluginTextDescription() {
        return IPluginDescription.GetPluginClassNameBase(this);
    }
}
