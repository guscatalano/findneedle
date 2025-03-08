using findneedle.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Implementations.Outputs;

public class NullOutput : SearchOutput
{
    public string GetClassName() => throw new NotImplementedException();
    public string GetFriendlyName() => throw new NotImplementedException();

    public string GetOutputFileName()
    {
        return "(the void)";
    }

    public string GetTextDescription() => throw new NotImplementedException();

    public void WriteAllOutput(List<SearchResult> result)
    {
        //do nothing
    }

    public void WriteOutput(SearchResult result)
    {
        //do nothing
    }
}
