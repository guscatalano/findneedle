using System;
using System.Collections.Generic;
using System.Linq;
using FindNeedleUX;

namespace FindNeedleUX.Services.PagedLogSource;

/// <summary>
/// <see cref="IPagedLogSource"/> backed by an in-memory <see cref="List{LogLine}"/>. Filter +
/// sort are computed lazily, then cached so repeated paging requests against the same filter/
/// sort are O(1) slice operations.
/// </summary>
public sealed class InMemoryPagedSource : IPagedLogSource
{
    private readonly List<LogLine> _all;
    private List<LogLine> _filtered;
    private FilterSpec _cachedFilter = FilterSpec.Empty;
    private SortSpec _cachedSort = SortSpec.None;
    private bool _cachedFromAll = true; // true => _filtered IS _all (no filter applied)

    public InMemoryPagedSource(IList<LogLine> all)
    {
        _all = all is List<LogLine> l ? l : new List<LogLine>(all ?? Array.Empty<LogLine>());
        _filtered = _all;
    }

    public int TotalCount => _all.Count;

    public int GetFilteredCount(FilterSpec filters)
    {
        EnsureCache(filters, _cachedSort);
        return _filtered.Count;
    }

    public List<LogLine> GetPage(FilterSpec filters, SortSpec sort, int offset, int limit)
    {
        EnsureCache(filters, sort);
        var total = _filtered.Count;
        if (offset < 0 || offset >= total || limit <= 0) return new List<LogLine>();
        var len = Math.Min(limit, total - offset);
        // GetRange copies into a small new list; sub-millisecond for typical pageSize.
        return _filtered.GetRange(offset, len);
    }

    public List<LogLine> GetLastPage(FilterSpec filters, SortSpec sort, int pageSize)
    {
        // In-memory slicing is O(1) regardless of offset, so the tail is just the last page via GetPage.
        EnsureCache(filters, sort);
        if (pageSize <= 0 || _filtered.Count == 0) return new List<LogLine>();
        int pages = (_filtered.Count + pageSize - 1) / pageSize;
        int lastOffset = (pages - 1) * pageSize; // start of the (possibly partial) last page
        return GetPage(filters, sort, lastOffset, pageSize);
    }

    public LogLine GetByRowId(long rowId)
    {
        // In-memory RowId is the load-order position (see LogLine ctor); scan the master list.
        for (int i = 0; i < _all.Count; i++)
            if (_all[i].RowId == rowId) return _all[i];
        return null;
    }

    public List<string> GetDistinctLevels()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in _all)
            if (!string.IsNullOrEmpty(l.Level)) set.Add(l.Level);
        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public Dictionary<string, int> GetLevelCounts(FilterSpec filters)
    {
        EnsureCache(filters, _cachedSort);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _filtered.Count; i++)
        {
            var lvl = _filtered[i].Level;
            var k = string.IsNullOrEmpty(lvl) ? "(none)" : lvl;
            counts.TryGetValue(k, out var c);
            counts[k] = c + 1;
        }
        return counts;
    }

    public void WalkAllFiltered(FilterSpec filters, SortSpec sort, Action<LogLine> onItem)
    {
        EnsureCache(filters, sort);
        for (int i = 0; i < _filtered.Count; i++) onItem(_filtered[i]);
    }

    public Dictionary<string, int> GetSourceCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < _all.Count; i++)
        {
            var k = string.IsNullOrEmpty(_all[i].Source) ? "" : _all[i].Source; // LogLine.Source = GetResultSource
            counts.TryGetValue(k, out var c);
            counts[k] = c + 1;
        }
        return counts;
    }

    public Dictionary<string, int> GetFieldCounts(string field, FilterSpec filters)
    {
        // Field → LogLine accessor (matches the SQLite column mapping: Provider=GetSource,
        // Source=GetResultSource, TaskName=TaskName).
        Func<LogLine, string> sel = (field ?? "").Trim().ToLowerInvariant() switch
        {
            "provider" => l => l.Provider,
            "taskname" or "task" => l => l.TaskName,
            "source" => l => l.Source,
            _ => null,
        };
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (sel == null) return counts;
        EnsureCache(filters, _cachedSort);
        for (int i = 0; i < _filtered.Count; i++)
        {
            var k = sel(_filtered[i]) ?? "";
            counts.TryGetValue(k, out var c);
            counts[k] = c + 1;
        }
        return counts;
    }

    public void Dispose() { /* no unmanaged resources */ }

    // ----- Streaming surface (in-memory backing is always a fixed snapshot) -----
    public bool IsLoading => false;
    public event Action? RowsAvailable { add { } remove { } }
    public void MarkLoadingComplete() { /* no-op: in-memory source is always already complete */ }

    // ----- caching -----
    private void EnsureCache(FilterSpec filters, SortSpec sort)
    {
        if (filters.Equals(_cachedFilter) && sort.Equals(_cachedSort)) return;

        _filtered = filters.IsEmpty ? new List<LogLine>(_all) : ApplyFilter(filters);
        _cachedFromAll = filters.IsEmpty && !sort.IsSorted;
        if (sort.IsSorted) ApplySort(sort, _filtered);

        _cachedFilter = filters;
        _cachedSort = sort;
    }

    private List<LogLine> ApplyFilter(FilterSpec f)
    {
        // Pre-cache decisions so the per-row body is just null checks.
        var search = NullIfBlank(f.Search);
        var provider = NullIfBlank(f.Provider);
        var taskName = NullIfBlank(f.TaskName);
        var message = NullIfBlank(f.Message);
        var source = NullIfBlank(f.Source);
        var level = NullIfBlank(f.Level);
        var from = f.FromTime;
        var to = f.ToTime;
        // Multi-select OR-sets (exact, case-insensitive). Null when unused.
        var providerSet = ToLookup(f.ProviderSet);
        var taskNameSet = ToLookup(f.TaskNameSet);
        var sourceSet = ToLookup(f.SourceSet);
        var levelSet = ToLookup(f.LevelSet);
        bool indexCouldMatchSearch = search != null && search.Length > 0 && IsAllAsciiDigit(search);

        var matches = new List<LogLine>(Math.Min(_all.Count, 4096));
        for (int i = 0; i < _all.Count; i++)
        {
            var line = _all[i];

            if (from.HasValue && line.LogTime < from.Value) continue;
            if (to.HasValue && line.LogTime > to.Value) continue;
            // A level set (multi-select) takes precedence over the single level field.
            if (levelSet != null) { if (!levelSet.Contains(line.Level ?? "")) continue; }
            else if (level != null && !string.Equals(line.Level, level, StringComparison.OrdinalIgnoreCase)) continue;
            // A set (multi-select) takes precedence over the substring field for that column.
            if (providerSet != null) { if (!providerSet.Contains(line.Provider ?? "")) continue; }
            else if (provider != null && !ContainsIgnoreCase(line.Provider, provider)) continue;
            if (taskNameSet != null) { if (!taskNameSet.Contains(line.TaskName ?? "")) continue; }
            else if (taskName != null && !ContainsIgnoreCase(line.TaskName, taskName)) continue;
            if (message != null && !ContainsIgnoreCase(line.Message, message)) continue;
            if (sourceSet != null) { if (!sourceSet.Contains(line.Source ?? "")) continue; }
            else if (source != null && !ContainsIgnoreCase(line.Source, source)) continue;

            if (search != null)
            {
                if (!ContainsIgnoreCase(line.Time, search) &&
                    !ContainsIgnoreCase(line.Provider, search) &&
                    !ContainsIgnoreCase(line.TaskName, search) &&
                    !ContainsIgnoreCase(line.Message, search) &&
                    !ContainsIgnoreCase(line.Source, search) &&
                    !ContainsIgnoreCase(line.Level, search) &&
                    !ContainsIgnoreCase(line.SearchableData, search) &&
                    !(indexCouldMatchSearch &&
                      line.Index.ToString().IndexOf(search, StringComparison.Ordinal) >= 0))
                {
                    continue;
                }
            }

            // Structured search-box query (evaluated against the row's fields).
            if (f.Query != null && !f.Query.Evaluate(name => LineField(line, name))) continue;

            matches.Add(line);
        }
        return matches;
    }

    /// <summary>Resolve a canonical query field name to this row's value. "*" = all searchable text.
    /// Field names mirror FindPluginCore.Searching.Query.LogQuery's canonical set.</summary>
    private static string LineField(LogLine l, string name) => name switch
    {
        "*" => l.SearchableData ?? l.Message ?? "",
        "message" => l.Message,
        "taskname" => l.TaskName,
        "provider" => l.Provider,
        "source" => l.Source,
        "level" => l.Level,
        "processid" => l.ProcessId,
        "threadid" => l.ThreadId,
        "eventid" => l.EventId,
        "channel" => l.Channel,
        "machinename" => l.MachineName,
        "username" => l.Username,
        "opcode" => l.OpCode,
        "time" => l.LogTime.ToString("o"),
        _ => "",
    };

    private static void ApplySort(SortSpec s, List<LogLine> list)
    {
        Comparison<LogLine> cmp = s.Column switch
        {
            "Index" => (a, b) => a.Index.CompareTo(b.Index),
            "Time" => (a, b) => a.LogTime.CompareTo(b.LogTime),
            "Provider" => (a, b) => string.Compare(a.Provider, b.Provider, StringComparison.OrdinalIgnoreCase),
            "TaskName" => (a, b) => string.Compare(a.TaskName, b.TaskName, StringComparison.OrdinalIgnoreCase),
            "Message" => (a, b) => string.Compare(a.Message, b.Message, StringComparison.OrdinalIgnoreCase),
            "Source" => (a, b) => string.Compare(a.Source, b.Source, StringComparison.OrdinalIgnoreCase),
            "Level" => (a, b) => string.Compare(a.Level, b.Level, StringComparison.OrdinalIgnoreCase),
            _ => null
        };
        if (cmp == null) return;
        if (s.Descending) { var inner = cmp; cmp = (a, b) => -inner(a, b); }
        list.Sort(cmp);
    }

    private static string NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    /// <summary>Build a case-insensitive lookup set from a multi-select filter list, or null if unused.</summary>
    private static HashSet<string> ToLookup(IReadOnlyList<string> values) =>
        values != null && values.Count > 0
            ? new HashSet<string>(values, System.StringComparer.OrdinalIgnoreCase)
            : null;
    private static bool ContainsIgnoreCase(string s, string needle) =>
        s != null && s.Length > 0 && s.Contains(needle, StringComparison.OrdinalIgnoreCase);
    private static bool IsAllAsciiDigit(string s)
    {
        for (int i = 0; i < s.Length; i++) { var c = s[i]; if (c < '0' || c > '9') return false; }
        return true;
    }
}
