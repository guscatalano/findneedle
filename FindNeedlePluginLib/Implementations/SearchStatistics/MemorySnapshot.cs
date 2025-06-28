using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedleCoreUtils;

namespace FindNeedlePluginLib;

public class MemorySnapshot
{

    public MemorySnapshot(Process? process = null)
    {
        if(process == null)
        {
            p = Process.GetCurrentProcess();
        } 
        else
        {
            p = process;
        }
    }

    private long privatememory = 0;
    private long totalmemory = 0;
    private DateTime when;
    private readonly Process p;
    private bool hasSnapped = false;

    public void Snap()
    {
        hasSnapped = true;
        when = DateTime.Now;
        p.Refresh();
        privatememory = p.PrivateMemorySize64;
        totalmemory = p.VirtualMemorySize64;
    }
    public long GetMemoryUsagePrivate()
    {
        if(!hasSnapped)
        {
            throw new Exception("MemorySnapshot has not been snapped yet.");
        }
        return privatememory;
    }
    public long GetMemoryUsageTotal()
    {
        if (!hasSnapped)
        {
            throw new Exception("MemorySnapshot has not been snapped yet.");
        }
        return totalmemory;
    }


    public string GetMemoryUsageFriendly()
    {
        if (!hasSnapped)
        {
            throw new Exception("MemorySnapshot has not been snapped yet.");
        }
        return " PrivateMemory (" + ByteUtils.BytesToFriendlyString(privatememory) + ") / Total Memory (" +
            ByteUtils.BytesToFriendlyString(totalmemory) + ").";
    }

    public DateTime GetSnapTime()
    {
        if (!hasSnapped)
        {
            throw new Exception("MemorySnapshot has not been snapped yet.");
        }
        return when;
    }

}

