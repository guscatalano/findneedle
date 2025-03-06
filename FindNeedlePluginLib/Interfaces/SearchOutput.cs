using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib.Interfaces;

namespace findneedle.Interfaces;

public interface SearchOutput : IPluginDescription
{
    public void WriteAllOutput(List<SearchResult> result);
    public void WriteOutput(SearchResult result);
    public string GetOutputFileName();
}
