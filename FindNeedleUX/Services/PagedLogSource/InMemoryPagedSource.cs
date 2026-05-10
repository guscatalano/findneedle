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

    public void Dispose() { /* no unmanaged resources */ }

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
        bool indexCouldMatchSearch = search != null && search.Length > 0 && IsAllAsciiDigit(search);

        var matches = new List<LogLine>(Math.Min(_all.Count, 4096));
        for (int i = 0; i < _all.Count; i++)
        {
            var line = _all[i];

            if (from.HasValue && line.LogTime < from.Value) continue;
            if (to.HasValue && line.LogTime > to.Value) continue;
            if (level != null && !string.Equals(line.Level, level, StringComparison.OrdinalIgnoreCase)) continue;
            if (provider != null && !ContainsIgnoreCase(line.Provider, provider)) continue;
            if (taskName != null && !ContainsIgnoreCase(line.TaskName, taskName)) continue;
            if (message != null && !ContainsIgnoreCase(line.Message, message)) continue;
            if (source != null && !ContainsIgnoreCase(line.Source, source)) continue;

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

            matches.Add(line);
        }
        return matches;
    }

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
    private static bool ContainsIgnoreCase(string s, string needle) =>
        s != null && s.Length > 0 && s.Contains(needle, StringComparison.OrdinalIgnoreCase);
    private static bool IsAllAsciiDigit(string s)
    {
        for (int i = 0; i < s.Length; i++) { var c = s[i]; if (c < '0' || c > '9') return false; }
        return true;
    }
}
