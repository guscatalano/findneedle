using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedlePluginLib;

public interface ISearchOutput : IPluginDescription, IDisposable
{
    public void WriteAllOutput(List<ISearchResult> result);
    public void WriteOutput(ISearchResult result);
    public string GetOutputFileName();
}
