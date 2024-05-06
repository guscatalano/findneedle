using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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



    public void ParseSummaryFile()
    {
        int maxtries = 10000;
        List<string> summary = new List<string>();
        
        while (maxtries > 0)
        {
            try
            {
                FileStream x = File.OpenRead(summaryfile);
                using (var reader = new StreamReader(x))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        summary.Add(line);
                    }
                }
                break;
            } catch(Exception ex)
            {
                Thread.Sleep(100);
                maxtries--;
                //tracefmt is still writing, wait
            }
        }
        if(maxtries == 0)
        {
            throw new Exception("Couldnt open summary file");
        }

        ProcessedFile = summary[0].Trim();
        TotalBuffersProcessed = Int32.Parse(summary[2].Substring(summary[2].LastIndexOf(" ")).Trim());
        TotalEventsProcessed = Int32.Parse(summary[3].Substring(summary[2].LastIndexOf(" ")).Trim());
        TotalEventsLost = Int32.Parse(summary[4].Substring(summary[2].LastIndexOf(" ")).Trim());
        TotalFormatErrors = Int32.Parse(summary[5].Substring(summary[2].LastIndexOf(" ")).Trim());
        TotalFormatsUnknown = Int32.Parse(summary[6].Substring(summary[2].LastIndexOf(" ")).Trim());
        TotalElapsedTime = summary[7].Replace("Elapsed", "").Replace("Time", "").Trim();
    }

    public string ProcessedFile
    {
    get; set; 
    }

    public int TotalBuffersProcessed
    {
        get; set;
    }

    public int TotalEventsProcessed
    {
        get; set;
    }

    public int TotalEventsLost
    {
        get; set;
    }

    public int TotalFormatErrors
    {
        get; set;
    }

    public int TotalFormatsUnknown
    {
        get; set;
    }

    public string TotalElapsedTime
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

        //Make this optional?
        result.ParseSummaryFile();

        return result;
    }
}
