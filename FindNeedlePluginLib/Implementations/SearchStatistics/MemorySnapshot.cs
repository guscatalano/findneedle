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
        // Working set (physical RAM in use), not VirtualMemorySize64 — the latter is reserved
        // address space and reads as terabytes on 64-bit, which is meaningless to the user.
        totalmemory = p.WorkingSet64;
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
        return " Private (" + ByteUtils.BytesToFriendlyString(privatememory) + ") / Working set (" +
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

