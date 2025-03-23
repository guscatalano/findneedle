using findneedle.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Implementations;

public class OutputToPlainFile : ISearchOutput
{
    readonly string filename = "";
    readonly FileStream? x;

    public OutputToPlainFile()
    {
        x = null;
    }

    public OutputToPlainFile(string filename)
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

    public string GetTextDescription()
    {
        return "Outputs the result to a text file without any formatting";
    }

    public string GetFriendlyName()
    {
        return "Output to plain file";
    }
    public string GetClassName()
    {
        var me = GetType();
        if (me.FullName == null)
        {
            throw new Exception("Fullname was null???");
        }
        else
        {
            return me.FullName;
        }
    }

    public void Dispose()
    {
        if (x != null)
        {
            x.Close();
        }
    }
}
