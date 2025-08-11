using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedlePluginLib;


public enum Level
{
    Catastrophic,
    Error,
    Warning,
    Info,
    Verbose, 
    Unknown // For use when we can't tell the level
}

public interface ISearchResult
{

    public const string NOT_SUPPORTED = "!NOT_SUPPORTED!"; //Use this in a search location where the request makes no sense, or throw.
    public DateTime GetLogTime();
    public string GetMachineName();
    public void WriteToConsole();
    public Level GetLevel();

    public string GetUsername();

    public string GetTaskName();
    public string GetOpCode();
    public string GetSource();

    public string GetSearchableData();
    public string GetMessage();


    public string GetResultSource();
}
