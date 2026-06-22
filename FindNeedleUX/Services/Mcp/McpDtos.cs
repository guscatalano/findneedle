using System;
using System.Collections.Generic;

namespace FindNeedleUX.Services.Mcp;

/// <summary>
/// Plain serializable data shapes returned by the MCP viewer bridge. Kept deliberately small and
/// JSON-friendly (no WinUI types) so the MCP host can hand them straight to the protocol layer.
/// The token-budget rule lives here: list rows carry a truncated Message; full text comes from a
/// single-record lookup with <c>full = true</c>.
/// </summary>
public sealed class RecordDto
{
    public long RowId { get; set; }
    public int Index { get; set; }
    public string Time { get; set; }
    public string Level { get; set; }
    public string Provider { get; set; }
    public string TaskName { get; set; }
    public string Source { get; set; }
    public string ProcessId { get; set; }
    public string ThreadId { get; set; }
    public string Message { get; set; }
    public string Tag { get; set; }      // tag category (Important/Question/Resolved/Note)
    public string TagText { get; set; }  // free-text note attached to the tag, if any

    // Only populated for a full single-record fetch.
    public string MachineName { get; set; }
    public string Username { get; set; }
    public string OpCode { get; set; }
    public string SearchableData { get; set; }
    // Correlation / detail columns — also where field-extraction enrichment writes (e.g. an
    // extracted HRESULT into EventId). Populated only for a full single-record fetch.
    public string EventId { get; set; }
    public string ProcessName { get; set; }
    public string ActivityId { get; set; }
    public string Keywords { get; set; }
    public string Channel { get; set; }
}

public sealed class PageDto
{
    public int Offset { get; set; }
    public int Limit { get; set; }
    public int TotalFiltered { get; set; }
    public int Total { get; set; }
    public List<RecordDto> Rows { get; set; } = new();
}

public sealed class ViewStateDto
{
    public string Search { get; set; }
    public string Provider { get; set; }
    public string TaskName { get; set; }
    public string Message { get; set; }
    public string Source { get; set; }
    public string Level { get; set; }
    public string FromTime { get; set; }
    public string ToTime { get; set; }
    public string SortColumn { get; set; }
    public bool SortDescending { get; set; }
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public int TotalFiltered { get; set; }
    public int Total { get; set; }
    public string DetailsMode { get; set; }
}

public sealed class SummaryDto
{
    public int Total { get; set; }
    public int TotalFiltered { get; set; }
    public string FromTime { get; set; }
    public string ToTime { get; set; }
    public Dictionary<string, int> Levels { get; set; } = new();
    public List<LocationDto> Sources { get; set; } = new();
    public List<string> Rules { get; set; } = new();
}

public sealed class LocationDto
{
    public string Name { get; set; }
    public string Description { get; set; }
    public bool IsEditable { get; set; }
}

public sealed class HistogramBucketDto
{
    public string Start { get; set; }
    public int Count { get; set; }
}

public sealed class ExportResultDto
{
    public string Path { get; set; }
    public int RowCount { get; set; }
}

/// <summary>
/// Health / orientation snapshot. Always succeeds (works with or without an open viewer) so an agent
/// can check what's available before calling viewer tools.
/// </summary>
public sealed class StatusDto
{
    public bool ServerRunning { get; set; }
    public int Port { get; set; }
    public bool HasViewer { get; set; }   // is a result viewer registered (viewer tools usable)?
    public int Locations { get; set; }    // data sources loaded in the workspace
    public int? Total { get; set; }        // total rows in the viewer (null if no viewer)
    public int? TotalFiltered { get; set; } // rows matching the current filter (null if no viewer)
}
