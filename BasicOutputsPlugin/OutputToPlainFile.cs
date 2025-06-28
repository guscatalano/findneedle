using FindNeedlePluginLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Implementations;

public class OutputToPlainFile : ISearchOutput
{
    private string filename = "";
    private FileStream? x;

    //Need this constructor for plugin
    public OutputToPlainFile()
    {
        Initialize();
    }

    public OutputToPlainFile(string filename)
    {
        Initialize(filename);
    }

    public void Initialize(string filename = "")
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            filename = System.IO.Path.GetTempPath() + Guid.NewGuid().ToString() + ".txt";
        }
 
        x = File.OpenWrite(filename);
        this.filename = filename;
    }

    ~OutputToPlainFile()
    {
        Dispose();
    }
    public void WriteAllOutput(List<ISearchResult> result)
    {
        foreach (ISearchResult item in result)
        {
            WriteOutput(item);
        }
    }

    public void WriteOutput(ISearchResult result)
    {
        if (x != null)
        {
            var info = new UTF8Encoding(true).GetBytes(result.GetMessage());
            x.Write(info);
        }
    }

    public string GetOutputFileName()
    {
        return filename;
    }

    public string GetPluginTextDescription()
    {
        return "Outputs the result to a text file without any formatting";
    }

    public string GetPluginFriendlyName()
    {
        return "Output to plain file";
    }
    public string GetPluginClassName()
    {
        return IPluginDescription.GetPluginClassNameBase(this);
    }

    public void Dispose()
    {
        if (x != null)
        {
            x.Close();
        }
    }
}
