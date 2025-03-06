using findneedle;
using findneedle.Interfaces;

namespace TestProcessorPlugin;

public class SampleNullOutput : SearchOutput
{

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
        return "SampleFileOutput"; 
    }

    public string GetTextDescription() {
        return "This is a sample plugin that just counts how many results there are";
    }

    public string GetOutputFileName() 
    {
        return "null";
    }
    public void WriteAllOutput(List<SearchResult> result)
    {

    }
    public void WriteOutput(SearchResult result) 
    {

    }
}
