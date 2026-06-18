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

    /// <summary>
    /// A stable, durable identifier for this row within the result set it came from. For the
    /// disk-backed store this is the SQLite <c>FilteredResults.Id</c>; it does not change when the
    /// viewer's filter/sort/paging changes (unlike the displayed row position). Used as the handle
    /// for record lookup and row tagging, including by the MCP server.
    /// Returns -1 for backends that have no stable id (callers fall back to load-order position).
    /// </summary>
    public long GetRowId() => -1;
}
