using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle
{

    public class MemorySnapshot
    {
        static readonly string[] SizeSuffixes =
                   { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

        static string SizeSuffix(long value, int decimalPlaces = 1)
        {
            if (decimalPlaces < 0) { throw new ArgumentOutOfRangeException("decimalPlaces"); }
            if (value < 0) { return "-" + SizeSuffix(-value, decimalPlaces); }
            if (value == 0) { return string.Format("{0:n" + decimalPlaces + "} bytes", 0); }

            // mag is 0 for bytes, 1 for KB, 2, for MB, etc.
            int mag = (int)Math.Log(value, 1024);

            // 1L << (mag * 10) == 2 ^ (10 * mag) 
            // [i.e. the number of bytes in the unit corresponding to mag]
            decimal adjustedSize = (decimal)value / (1L << (mag * 10));

            // make adjustment when the value is large enough that
            // it would round up to 1000 or more
            if (Math.Round(adjustedSize, decimalPlaces) >= 1000)
            {
                mag += 1;
                adjustedSize /= 1024;
            }

            return string.Format("{0:n" + decimalPlaces + "} {1}",
                adjustedSize,
                SizeSuffixes[mag]);
        }

        long privatememory = 0;
        long gcmemory = 0;
        DateTime when;
        Process p;
        public MemorySnapshot(Process p)
        {
            this.p = p;
        }

        public void Snap()
        {
            when = DateTime.Now;
            p.Refresh();
            privatememory = p.PrivateMemorySize64;
            gcmemory = GC.GetTotalMemory(false);
        }

        public string GetMemoryUsage()
        {
            return " PrivateMemory (" + SizeSuffix(privatememory) + ") / GC Memory (" + SizeSuffix(gcmemory) + ")."; 
        }

        public DateTime GetSnapTime()
        {
            return when;
        }
        
    }

    public class SearchStatistics
    {
        SearchQuery q;
        Process proc;
        public SearchStatistics(SearchQuery query) 
        { 
            q = query;
            proc = Process.GetCurrentProcess();
            atLoad = new MemorySnapshot(proc);
            atSearch = new MemorySnapshot(proc);
            atLaunch = new MemorySnapshot(proc);
            atLaunch.Snap();
        }

        int totalRecordsSearch = 0;
        int totalRecordsLoaded = 0;
        MemorySnapshot atLaunch;
        MemorySnapshot atLoad;
        MemorySnapshot atSearch;

        

        public void LoadedAll()
        {
            totalRecordsLoaded = 0;
            foreach (SearchLocation loc in q.GetLocations())
            {
                totalRecordsLoaded += loc.numRecordsInMemory;
            }

            atLoad.Snap();
        }

        public void Searched()
        {
            totalRecordsSearch = 0;
            foreach (SearchLocation loc in q.GetLocations())
            {
                totalRecordsSearch += loc.numRecordsInLastResult;
            }
            atSearch.Snap();
        }

        public void ReportToConsole()
        {
            TimeSpan timeToLoad = atLoad.GetSnapTime() - atLaunch.GetSnapTime();
            TimeSpan timeToSearch = atSearch.GetSnapTime() - atLoad.GetSnapTime();
            TimeSpan totalTime = atSearch.GetSnapTime() - atLaunch.GetSnapTime();
            Console.WriteLine("Memory at launch: " + atLoad.GetMemoryUsage());
            Console.WriteLine("Total records when loaded (" + totalRecordsLoaded + ") with" + atLoad.GetMemoryUsage());
            Console.WriteLine("Total records after search (" + totalRecordsSearch + ") with" + atSearch.GetMemoryUsage());
            Console.WriteLine("Took " + timeToLoad.TotalSeconds + " second(s) to load.");
            Console.WriteLine("Took " + timeToSearch.TotalSeconds + " second(s) to search.");
            Console.WriteLine("Took " + totalTime.TotalSeconds + " second(s) total.");
        }
    }
}
