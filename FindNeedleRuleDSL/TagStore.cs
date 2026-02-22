using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FindNeedlePluginLib;

namespace findneedle.RuleDSL;

/// <summary>
/// Stores tags applied to ISearchResult instances for the duration of a run.
/// Uses ConditionalWeakTable so entries do not prevent GC of results.
/// </summary>
public static class TagStore
{
    private static ConditionalWeakTable<ISearchResult, List<string>> _table = new ConditionalWeakTable<ISearchResult, List<string>>();

    public static void AddTag(ISearchResult result, string tag)
    {
        if (result == null || string.IsNullOrEmpty(tag)) return;
        var list = _table.GetOrCreateValue(result);
        if (!list.Contains(tag, StringComparer.OrdinalIgnoreCase))
            list.Add(tag);
    }

    public static IReadOnlyList<string> GetTags(ISearchResult result)
    {
        if (result == null) return Array.Empty<string>();
        if (_table.TryGetValue(result, out var list)) return list.AsReadOnly();
        return Array.Empty<string>();
    }

    public static void Reset()
    {
        _table = new ConditionalWeakTable<ISearchResult, List<string>>();
    }
}
