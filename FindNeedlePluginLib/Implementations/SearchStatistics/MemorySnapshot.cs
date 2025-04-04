using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedleCoreUtils;

namespace FindNeedlePluginLib.Implementations.SearchStatistics;

public class MemorySnapshot
{


    long privatememory = 0;
    long gcmemory = 0;
    DateTime when;
    readonly Process p;
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
        return " PrivateMemory (" + ByteUtils.BytesToFriendlyString(privatememory) + ") / GC Memory (" + ByteUtils.BytesToFriendlyString(gcmemory) + ").";
    }

    public DateTime GetSnapTime()
    {
        return when;
    }

}

