using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedleCoreUtils;

namespace FindNeedlePluginLib.Implementations.SearchStatistics;

public class MemorySnapshot(Process p)
{
    private long privatememory = 0;
    private long totalmemory = 0;
    private DateTime when;
    private readonly Process p = p;

    public void Snap()
    {
        when = DateTime.Now;
        p.Refresh();
        privatememory = p.PrivateMemorySize64;
        totalmemory = p.VirtualMemorySize64;
    }
    public long GetMemoryUsagePrivate()
    {
        return privatememory;
    }
    public long GetMemoryUsageTotal()
    {
        return totalmemory;
    }


    public string GetMemoryUsageFriendly()
    {
        return " PrivateMemory (" + ByteUtils.BytesToFriendlyString(privatememory) + ") / Total Memory (" +
            ByteUtils.BytesToFriendlyString(totalmemory) + ").";
    }

    public DateTime GetSnapTime()
    {
        return when;
    }

}

