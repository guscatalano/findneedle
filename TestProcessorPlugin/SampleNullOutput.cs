using System.Diagnostics.CodeAnalysis;
using FindNeedlePluginLib;

namespace TestProcessorPlugin;

[ExcludeFromCodeCoverage]
public class SampleNullOutput : ISearchOutput, IPluginDescription
{

    public string GetPluginClassName()
    {
        return IPluginDescription.GetPluginClassNameBase(this);
    }

    public string GetPluginFriendlyName() {
        return "SampleFileOutput"; 
    }

    public string GetPluginTextDescription() {
        return "This is a sample plugin that just counts how many results there are";
    }

    public string GetOutputFileName() 
    {
        return "null";
    }
    public void WriteAllOutput(List<ISearchResult> result)
    {

    }
    public void WriteOutput(ISearchResult result) 
    {

    }

    public void Dispose() 
    {
    }

}
