using findneedle.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Implementations.Outputs;

public class NullOutput : ISearchOutput
{
    public string GetClassName() => throw new NotImplementedException();
    public string GetFriendlyName() => throw new NotImplementedException();

    public string GetOutputFileName()
    {
        return "(the void)";
    }

    public string GetTextDescription() => throw new NotImplementedException();

    public void WriteAllOutput(List<ISearchResult> result)
    {
        //do nothing
    }

    public void WriteOutput(ISearchResult result)
    {
        //do nothing
    }
}
