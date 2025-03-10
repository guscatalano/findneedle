using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib.Interfaces;

namespace findneedle.Interfaces;

public interface ISearchOutput : IPluginDescription
{
    public void WriteAllOutput(List<ISearchResult> result);
    public void WriteOutput(ISearchResult result);
    public string GetOutputFileName();
}
