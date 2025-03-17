using findneedle;
using findneedle.Interfaces;

namespace TestProcessorPlugin;

public class SampleNullOutput : ISearchOutput
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
