﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib.Interfaces;

namespace findneedle;

//Defines how deep to search a given location. Deepest might imply pre-loading data that may not matter
public enum SearchLocationDepth
{
    Shallow = 0,
    Intermediate = 1,
    Deep = 2,
    Crush = 3 //Load everything
}

public abstract class SearchLocation
{
    public int numRecordsInLastResult
    { get;
        set;
    }
    public int numRecordsInMemory
    {
        get; set;
    }

    public SearchLocationDepth depth
    {
    get; set; }


    public abstract void LoadInMemory(bool prefilter, ISearchQuery searchQuery);

    public abstract List<SearchResult> Search(ISearchQuery searchQuery);

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

}