using findneedle.Interfaces;
using FindNeedlePluginLib.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Implementations.Outputs;

public class NullOutput : ISearchOutput
{

    public void Dispose()
    {
        //do nothing
    }
    public string GetPluginClassName()
    {
        return IPluginDescription.GetPluginClassNameBase(this);
    }
    public string GetPluginFriendlyName() {
        return "Null Output";
    }

    public string GetOutputFileName()
    {
        return "(the void)";
    }

    public string GetPluginTextDescription() {
        return "This plugin just outputs nowhere";
    }

    public void WriteAllOutput(List<ISearchResult> result)
    {
        //do nothing
    }

    public void WriteOutput(ISearchResult result)
    {
        //do nothing
    }
}
