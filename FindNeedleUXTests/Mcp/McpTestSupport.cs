using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FindNeedlePluginLib;
using FindNeedleUX.Services.Mcp;

namespace FindNeedleUXTests.Mcp;

/// <summary>Minimal ISearchResult for building LogLines in MCP tests.</summary>
internal sealed class FakeResult : ISearchResult
{
    private readonly string _msg;
    private readonly string _source;
    private readonly Level _level;
    private readonly long _id;
    public FakeResult(string msg, string source = "prov", Level level = Level.Info, long id = -1)
    { _msg = msg; _source = source; _level = level; _id = id; }
    public DateTime GetLogTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public string GetMachineName() => "M";
    public void WriteToConsole() { }
    public Level GetLevel() => _level;
    public string GetUsername() => "u";
    public string GetTaskName() => "task";
    public string GetOpCode() => "";
    public string GetSource() => _source;
    public string GetSearchableData() => _msg;
    public string GetMessage() => _msg;
    public string GetResultSource() => "rs";
    public long GetRowId() => _id;
}

/// <summary>
/// Records calls and returns canned DTOs so bridge/server tests can assert delegation + wire format
/// without a real WinUI viewer.
/// </summary>
internal sealed class FakeViewerController : IMcpViewerController
{
    public int GetViewCalls;
    public long LastRecordId = long.MinValue;
    public string LastTag;
    public string LastTagText;
    public long LastTagId = long.MinValue;
    public string LastSortColumn;

    public Task<ViewStateDto> GetViewAsync()
    {
        GetViewCalls++;
        return Task.FromResult(new ViewStateDto { Search = "hello", CurrentPage = 2, PageSize = 50, TotalFiltered = 7, Total = 9 });
    }

    public Task<PageDto> GetPageAsync(int? offset, int limit)
    {
        var p = new PageDto { Offset = offset ?? 0, Limit = limit, TotalFiltered = 7, Total = 9 };
        p.Rows.Add(new RecordDto { RowId = 100, Index = 0, Message = "row-zero", Level = "Error" });
        return Task.FromResult(p);
    }

    public Task<RecordDto> GetRecordAsync(long rowId)
    {
        LastRecordId = rowId;
        return Task.FromResult(new RecordDto { RowId = rowId, Message = "full message", MachineName = "M" });
    }

    public Task<SummaryDto> GetSummaryAsync()
        => Task.FromResult(new SummaryDto { Total = 9, TotalFiltered = 7 });

    public Task<List<HistogramBucketDto>> GetHistogramAsync(int buckets)
        => Task.FromResult(new List<HistogramBucketDto> { new() { Start = "2026-01-01T00:00:00Z", Count = 7 } });

    public Task<LogAnalysis.FacetResult> GetFacetsAsync(string field, int limit, int sampleCap)
        => Task.FromResult(new LogAnalysis.FacetResult(field, 1, 1, false,
            new List<LogAnalysis.Facet> { new("x", 1) }));

    public Task<LogAnalysis.PatternResult> GetTopPatternsAsync(int limit, int sampleCap)
        => Task.FromResult(new LogAnalysis.PatternResult(1, 1, false,
            new List<LogAnalysis.Pattern> { new("{n} thing", 1, "1 thing") }));

    public Task<int> SetFilterAsync(string search, string provider, string taskName, string message,
        string source, string level, string fromTime, string toTime)
        => Task.FromResult(42);

    public Task<int> ClearFiltersAsync() => Task.FromResult(9);

    public bool LastRuleFilterOn;
    public Task<int> SetRuleViewFilterAsync(bool on) { LastRuleFilterOn = on; return Task.FromResult(on ? 5 : 17); }
    public Task<bool> WaitForLoadAsync(int timeoutMs) => Task.FromResult(true);

    public Task SetSortAsync(string column, bool descending) { LastSortColumn = column; return Task.CompletedTask; }
    public Task GoToPageAsync(int page) => Task.CompletedTask;
    public Task SetPageSizeAsync(int pageSize) => Task.CompletedTask;
    public Task<bool> SelectRowAsync(long rowId) => Task.FromResult(true);
    public Task<bool> TagRowAsync(long rowId, string tag, string text) { LastTagId = rowId; LastTag = tag; LastTagText = text; return Task.FromResult(true); }
    public Task<bool> ClearTagAsync(long rowId) => Task.FromResult(true);

    public List<TagCountDto> TagCountsResult = new() { new TagCountDto { Tag = "Important", Count = 2 } };
    public Task<List<TagCountDto>> GetTagCountsAsync() => Task.FromResult(TagCountsResult);

    public string LastTaggedFilter = "<unset>";
    public Task<List<RecordDto>> GetTaggedRowsAsync(string tag)
    {
        LastTaggedFilter = tag;
        return Task.FromResult(new List<RecordDto> { new() { RowId = 100, Message = "tagged" } });
    }

    public long LastContextRowId = long.MinValue;
    public int LastContextBefore, LastContextAfter;
    public Task<ContextDto> GetContextAsync(long rowId, int before, int after)
    {
        LastContextRowId = rowId; LastContextBefore = before; LastContextAfter = after;
        return Task.FromResult(new ContextDto
        {
            Found = true, RowId = rowId, TargetIndex = 10,
            Target = new RecordDto { RowId = rowId },
            Rows = { new RecordDto { RowId = rowId } },
        });
    }
    public Task ReloadAsync() => Task.CompletedTask;
    public Task SetDetailsModeAsync(string mode) => Task.CompletedTask;
    public Task<ExportResultDto> ExportAsync(string format, string destPath)
        => Task.FromResult(new ExportResultDto { Path = destPath ?? "C:/tmp/x.csv", RowCount = 7 });
}
