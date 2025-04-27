using findneedle;
using findneedle.Interfaces;
using FindNeedlePluginLib.Interfaces;

namespace SessionManagementProcessor;

public class SessionManagementProcessor : IResultProcessor, IPluginDescription
{
    public string GetPluginClassName()
    {
        return IPluginDescription.GetPluginClassNameBase(this);
    }
    public string GetPluginFriendlyName() => "Session Management Processor";
    public string GetPluginTextDescription() => "This plugin manages the session of the search query.";
    public void Dispose()
    {
    }

    public string GetDescription() {
        return "Djfodjsf"; 
    }
    public string GetOutputFile(string optionalOutputFolder = "") {
        return "";
    }
    public string GetOutputText()
    {
        return "Output text";
    }
    public void ProcessResults(List<ISearchResult> results)
    {
        foreach(var ret in results)
        {
            if (ret.GetMessage().Contains("winlogon"))
            {

            }
        }
    }
}
