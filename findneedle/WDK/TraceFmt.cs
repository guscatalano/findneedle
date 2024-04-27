using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.WDK;

public class TraceFmtResult
{
    public string outputfile
    {
        get;set;
    }
    public string summaryfile
    {
        get; set;
    }

    
}

public class TraceFmt
{
    public static TraceFmtResult ParseSimpleETL(string etl, string temppath)
    {
        if (!File.Exists(WDKFinder.GetTraceFmtPath()))
        {
            throw new Exception("Cant find tracefmt");
        }

        if (!File.Exists(etl))
        {
            throw new Exception("Cant find etl");
        }
        TraceFmtResult result = new TraceFmtResult();

        ProcessStartInfo st = new ProcessStartInfo();
        st.FileName = WDKFinder.GetTraceFmtPath();
        st.Arguments = etl;
        st.WindowStyle = ProcessWindowStyle.Hidden;
        st.WorkingDirectory = temppath;
        Process? p = Process.Start(st);
        if(p == null)
        {
            throw new Exception("???");
        }
        p.Start();
        p.WaitForExit();
        if(p.ExitCode != 0)
        {
            throw new Exception("exit code was not 0 for tracefmt!");
        }


        result.outputfile = Path.Combine(temppath, "FmtFile.txt");
        result.summaryfile = Path.Combine(temppath, "FmtSum.txt");
        if (!File.Exists(result.outputfile))
        {
            throw new Exception("FmtFile output was not there!");
        }

        if (!File.Exists(result.summaryfile))
        {
            throw new Exception("FmtSum output was not there!");
        }

       


        return result;
    }
}
