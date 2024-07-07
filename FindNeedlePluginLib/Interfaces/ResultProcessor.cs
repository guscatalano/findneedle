using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib.Interfaces;

namespace findneedle.Interfaces;
public interface ResultProcessor : IPluginDescription
{
    public void ProcessResults(List<SearchResult> results);
    public string GetOutputFile(string optionalOutputFolder = "");

    public string GetOutputText();
}
