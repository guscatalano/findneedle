using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib.Interfaces;

namespace findneedle.Interfaces;
public interface IResultProcessor : IPluginDescription
{
    public void ProcessResults(List<ISearchResult> results);
    public string GetOutputFile(string optionalOutputFolder = "");

    public string GetOutputText();
}
