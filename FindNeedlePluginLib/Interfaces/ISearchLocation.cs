﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace FindNeedlePluginLib;

//Defines how deep to search a given location. Deepest might imply pre-loading data that may not matter
public enum SearchLocationDepth
{
    Shallow = 0,
    Intermediate = 1,
    Deep = 2,
    Crush = 3 //Load everything
}

public abstract class ISearchLocation: IReportStatistics
{
    public int numRecordsInLastResult
    { 
        get; set;
    }
    public int numRecordsInMemory
    {
        get; set;
    }

    public SearchLocationDepth depth
    {
        get; set; 
    }

    // Make CancellationToken optional
    public abstract void LoadInMemory(System.Threading.CancellationToken cancellationToken = default);

    // Remove ISearchQuery from Search signature
    public abstract List<ISearchResult> Search(System.Threading.CancellationToken cancellationToken = default);

    public void SetSearchDepth(SearchLocationDepth depth)
    {
        this.depth = depth;
    }
    public SearchLocationDepth GetSearchDepth()
    {
        return this.depth;
    }

    public abstract string GetDescription();
    public abstract string GetName();
    public abstract void ClearStatistics();
    public abstract List<ReportFromComponent> ReportStatistics();
}
