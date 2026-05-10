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

    /// <param name="storage">Underlying SQLite storage.</param>
    /// <param name="ownsStorage">If true, this source disposes the storage when it itself is disposed.</param>
    public SqlitePagedSource(SqliteStorage storage, bool ownsStorage = false)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _ownsStorage = ownsStorage;
    }

    public int TotalCount => _storage.GetStatistics().filteredRecordCount;

    public int GetFilteredCount(FilterSpec filters) => _storage.GetFilteredCount(ToInput(filters));

    public List<FindNeedleUX.LogLine> GetPage(FilterSpec filters, SortSpec sort, int offset, int limit)
    {
        var rows = _storage.GetFilteredPage(ToInput(filters), ToSortInput(sort), offset, limit);
        var list = new List<FindNeedleUX.LogLine>(rows.Count);
        for (int i = 0; i < rows.Count; i++)
            list.Add(new FindNeedleUX.LogLine(rows[i], offset + i));
        return list;
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
        if (_ownsStorage) _storage.Dispose();
    }

    // ----- mappers -----

    private static SqliteStorage.FilterInput ToInput(FilterSpec f)
    {
        if (f == null) return new SqliteStorage.FilterInput();

        // Level is stored in SQL as the underlying enum int. Map the user's level-string back.
        int? levelInt = null;
        if (!string.IsNullOrEmpty(f.Level)
            && Enum.TryParse<Level>(f.Level, ignoreCase: true, out var parsed))
        {
            levelInt = (int)parsed;
        }

        return new SqliteStorage.FilterInput
        {
            Search = f.Search,
            Provider = f.Provider,
            TaskName = f.TaskName,
            Message = f.Message,
            Source = f.Source,
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
