using System;
using System.Collections.Generic;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using FindPluginCore.Implementations.Storage;

namespace FindNeedleUX.Services.PagedLogSource;

/// <summary>
/// <see cref="IPagedLogSource"/> backed by a <see cref="SqliteStorage"/>. Each method translates
/// the in-process FilterSpec / SortSpec into a parameterized SQL query against the FilteredResults
/// table. No row materialization beyond the requested page.
/// </summary>
public sealed class SqlitePagedSource : IPagedLogSource
{
    private readonly SqliteStorage _storage;
    private readonly bool _ownsStorage;
    private readonly Action<int> _onStorageRows;
    private bool _isLoading;

    /// <param name="storage">Underlying SQLite storage.</param>
    /// <param name="ownsStorage">If true, this source disposes the storage when it itself is disposed.</param>
    /// <param name="startInLoadingState">
    /// If true, the source begins in <c>IsLoading=true</c> and subscribes to the storage's
    /// <c>FilteredRowsAdded</c> event so writes from a concurrent producer surface as
    /// <see cref="RowsAvailable"/>. Producers must call <see cref="MarkLoadingComplete"/> when done.
    /// </param>
    public SqlitePagedSource(SqliteStorage storage, bool ownsStorage = false, bool startInLoadingState = false)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _ownsStorage = ownsStorage;
        _isLoading = startInLoadingState;

        // Hold the delegate so we can unsubscribe cleanly in Dispose.
        _onStorageRows = _ => RowsAvailable?.Invoke();
        if (startInLoadingState)
        {
            _storage.FilteredRowsAdded += _onStorageRows;
        }
    }

    public bool IsLoading => _isLoading;
    public event Action? RowsAvailable;

    public void MarkLoadingComplete()
    {
        if (!_isLoading) return;
        _isLoading = false;
        _storage.FilteredRowsAdded -= _onStorageRows;
        // One final fire so the viewer does a definitive refresh on the now-stable data.
        RowsAvailable?.Invoke();
    }

    public int TotalCount => _storage.GetStatistics().filteredRecordCount;

    public int GetFilteredCount(FilterSpec filters) => _storage.GetFilteredCount(ToInput(filters));

    public List<FindNeedleUX.LogLine> GetPage(FilterSpec filters, SortSpec sort, int offset, int limit)
    {
        var rows = _storage.GetFilteredPage(ToInput(filters), ToSortInput(sort), offset, limit);
        var list = new List<FindNeedleUX.LogLine>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            // Index shows the stable load-order line number (the SQLite Id), not the per-page position
            // — otherwise it re-labels 0,1,2… for every sort direction and "sort by Index" looks like a
            // no-op even though the rows do reorder. Falls back to page position if there's no id.
            long id = rows[i].GetRowId();
            list.Add(new FindNeedleUX.LogLine(rows[i], id >= 0 ? (int)id : offset + i));
        }
        return list;
    }

    public FindNeedleUX.LogLine GetByRowId(long rowId)
    {
        var r = _storage.GetById(rowId);
        // Index is unknown out of paging context; -1 signals "no display position".
        return r == null ? null : new FindNeedleUX.LogLine(r, -1);
    }

    public List<string> GetDistinctLevels()
    {
        var ints = _storage.GetDistinctLevels();
        var names = new List<string>(ints.Count);
        foreach (var n in ints) names.Add(((Level)n).ToString());
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public Dictionary<string, int> GetLevelCounts(FilterSpec filters)
    {
        var raw = _storage.GetLevelCounts(ToInput(filters));
        var named = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in raw) named[((Level)kv.Key).ToString()] = kv.Value;
        return named;
    }

    public Dictionary<string, int> GetSourceCounts() => _storage.GetSourceCounts();

    public void WalkAllFiltered(FilterSpec filters, SortSpec sort, Action<FindNeedleUX.LogLine> onItem)
    {
        // Stream in pages so we never hold the whole filtered set in memory at once.
        const int BatchSize = 5000;
        var input = ToInput(filters);
        var sortIn = ToSortInput(sort);
        int offset = 0;
        while (true)
        {
            var rows = _storage.GetFilteredPage(input, sortIn, offset, BatchSize);
            if (rows.Count == 0) break;
            for (int i = 0; i < rows.Count; i++)
                onItem(new FindNeedleUX.LogLine(rows[i], offset + i));
            if (rows.Count < BatchSize) break;
            offset += rows.Count;
        }
    }

    public void Dispose()
    {
        // Always unsubscribe — _isLoading may already be false (MarkLoadingComplete ran), in
        // which case this is a no-op.
        try { _storage.FilteredRowsAdded -= _onStorageRows; } catch { /* ignore */ }
        if (_ownsStorage) _storage.Dispose();
    }

    // ----- mappers -----

    private static SqliteStorage.FilterInput ToInput(FilterSpec f)
    {
        if (f == null) return new SqliteStorage.FilterInput();

        // Level is stored in SQL as the underlying enum int. Map the user's level-string back.
        // A non-empty level that isn't a real Level enum value (e.g. "Critical"/"Debug" from a stale
        // preset, or a typo over MCP) must match NOTHING — not silently drop the filter and show every
        // row. -1 maps to no enum member, so "Level = -1" yields zero results.
        int? levelInt = null;
        if (!string.IsNullOrEmpty(f.Level))
        {
            levelInt = Enum.TryParse<Level>(f.Level, ignoreCase: true, out var parsed) ? (int)parsed : -1;
        }

        return new SqliteStorage.FilterInput
        {
            Search = f.Search,
            Provider = f.Provider,
            TaskName = f.TaskName,
            Message = f.Message,
            Source = f.Source,
            ProviderSet = f.ProviderSet,
            TaskNameSet = f.TaskNameSet,
            SourceSet = f.SourceSet,
            Query = f.Query,
            LevelInt = levelInt,
            // The viewer's time-range filter compares against LogTime stored as ISO 8601 round-trip
            // ("o") strings. ISO strings sort lexically same as chronologically, so >= / <= work.
            LogTimeFrom = f.FromTime?.ToString("o"),
            LogTimeTo = f.ToTime?.ToString("o"),
        };
    }

    private static SqliteStorage.SortInput ToSortInput(SortSpec s) =>
        s == null ? new SqliteStorage.SortInput()
                  : new SqliteStorage.SortInput { Column = s.Column, Descending = s.Descending };
}
