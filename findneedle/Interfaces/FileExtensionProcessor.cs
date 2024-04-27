using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Interfaces;
public interface FileExtensionProcessor
{

    public void LoadInMemory();
    public void DoPreProcessing();

    public List<SearchResult> GetResults();
}
