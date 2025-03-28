﻿using findneedle.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Implementations.Outputs;

public class NullOutput : ISearchOutput
{

    public void Dispose()
    {
        //do nothing
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
    public string GetFriendlyName() {
        return "Null Output";
    }

    public string GetOutputFileName()
    {
        return "(the void)";
    }

    public string GetTextDescription() {
        return "This plugin just outputs nowhere";
    }

    public void WriteAllOutput(List<ISearchResult> result)
    {
        //do nothing
    }

    public void WriteOutput(ISearchResult result)
    {
        //do nothing
    }
}
