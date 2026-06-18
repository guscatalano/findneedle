using System.Collections.Generic;
using System.Threading.Tasks;

namespace FindNeedleUX.Services.Mcp;

/// <summary>
/// The surface the active result viewer exposes to the MCP bridge so an agent can read and drive
/// the same live view the user sees. Implemented by <c>NativeResultsPage</c>, which marshals every
/// call onto the UI thread (so the grid updates as the agent acts). The bridge holds at most one
/// registered controller at a time (the last-loaded viewer); when none is registered, the bridge's
/// viewer tools report "no active view".
///
/// All methods are async because they hop to the UI thread and await completion.
/// </summary>
public interface IMcpViewerController
{
    Task<ViewStateDto> GetViewAsync();

    /// <summary>A page of the current filtered/sorted result. Null offset = the viewer's current page.</summary>
    Task<PageDto> GetPageAsync(int? offset, int limit);

    /// <summary>One row by stable id, with all fields (full Message). Null if no such row.</summary>
    Task<RecordDto> GetRecordAsync(long rowId);

    Task<SummaryDto> GetSummaryAsync();

    Task<List<HistogramBucketDto>> GetHistogramAsync(int buckets);

    /// <summary>Set any subset of filters (null = leave unchanged, "" = clear). Returns new filtered count.</summary>
    Task<int> SetFilterAsync(string search, string provider, string taskName, string message,
        string source, string level, string fromTime, string toTime);

    Task<int> ClearFiltersAsync();

    Task SetSortAsync(string column, bool descending);

    Task GoToPageAsync(int page);

    Task SetPageSizeAsync(int pageSize);

    Task<bool> SelectRowAsync(long rowId);

    /// <summary>
    /// Tag a row. <paramref name="tag"/> is the category (null/blank keeps the row's existing
    /// category). <paramref name="text"/> is a free-text note (null keeps the existing note; "" or a
    /// value sets it). Returns false if there's no valid category to apply.
    /// </summary>
    Task<bool> TagRowAsync(long rowId, string tag, string text);

    Task<bool> ClearTagAsync(long rowId);

    Task SetDetailsModeAsync(string mode);

    /// <summary>
    /// Export the current filtered/sorted set (visible columns) to a file. <paramref name="format"/>
    /// is csv/json/xml; null/blank <paramref name="destPath"/> writes to a timestamped temp file.
    /// Returns the path written and the row count (Path null on failure).
    /// </summary>
    Task<ExportResultDto> ExportAsync(string format, string destPath);
}
