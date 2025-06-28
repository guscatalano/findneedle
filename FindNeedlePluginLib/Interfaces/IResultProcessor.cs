using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedlePluginLib;
public interface IResultProcessor
{
    public void ProcessResults(List<ISearchResult> results);
    public string GetOutputFile(string optionalOutputFolder = "");

    public string GetDescription();
    public string GetOutputText();
}
