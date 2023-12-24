using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Interfaces;
public interface ResultProcessor
{
    public void ProcessResults(List<SearchResult> results);
    public string GetOutputFile(string optionalOutputFolder = "");
}
