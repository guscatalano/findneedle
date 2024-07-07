using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.Interfaces;

namespace findneedle.Implementations.ResultProcessors;
public class WatsonCrashProcessor : ResultProcessor
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

    public string GetFriendlyName()
    {
        return "Watson Crash Processor";
    }

    public string GetOutputFile(string optionalOutputFolder = "")
    {
        return "";
    }

    public string GetOutputText() {
        return "";
    }

    public string GetTextDescription() 
    {
        return "Finds application crashes in the logs";
    }

    public void ProcessResults(List<SearchResult> results)
    {
        List<SearchResult> resultList = new List<SearchResult>();
        foreach(SearchResult result in results)
        {
            if(result.GetSearchableData().Contains("A .NET application failed."))
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
