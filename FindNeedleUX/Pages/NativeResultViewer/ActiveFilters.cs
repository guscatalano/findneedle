using System;
using System.Collections.Generic;
using System.Linq;

namespace FindNeedleUX.Pages.NativeResultViewer;

/// <summary>
/// One constraint currently narrowing the results view, as the filter pane shows it: a short human
/// label and the action that removes exactly that one. <see cref="Kind"/>/<see cref="Key"/> identify
/// it for the host (which supplies <see cref="Clear"/>) and for tests.
/// </summary>
public sealed class ActiveFilter
{
    /// <summary>"search" | "time" | "level" | "field" | "rules" | "quickrule".</summary>
    public string Kind { get; init; } = "";

    /// <summary>Sub-identity within the kind: the field's canonical name, "from"/"to"/"preset",
    /// the quick rule's index… Empty when the kind is already unique.</summary>
    public string Key { get; init; } = "";

    /// <summary>Short pill caption, e.g. <c>level in (Error, Warning)</c>.</summary>
    public string Label { get; init; } = "";

    /// <summary>Clears just this constraint. Null when the host supplied no clear action.</summary>
    public Action Clear { get; init; }

    public override string ToString() => Label;
}

/// <summary>A session quick rule as the pane lists it (see FindNeedleUX.Services.ViewerQuickRule).</summary>
public sealed class QuickRuleView
{
    public string Label { get; init; } = "";
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// A plain snapshot of every filter input the viewer can have active. Deliberately free of WinUI and
/// of the view model, so <see cref="ActiveFilterCatalog.Build"/> is a pure function the unit tests can
/// exercise (the UX test project builds with WinUI disabled).
/// </summary>
public sealed class ActiveFilterState
{
    // Toolbar search box (plain substring or a structured query — either way, one constraint).
    public string Search { get; init; } = "";

    // Per-field substring filters.
    public string Provider { get; init; } = "";
    public string TaskName { get; init; } = "";
    public string Message { get; init; } = "";
    public string Source { get; init; } = "";
    public string Level { get; init; } = "";

    // "Pick from values" multi-select OR-sets. A set and its substring filter can both be set; each is
    // its own removable constraint (that is also how the old badge counted them).
    public IReadOnlyList<string> ProviderSet { get; init; }
    public IReadOnlyList<string> TaskNameSet { get; init; }
    public IReadOnlyList<string> SourceSet { get; init; }
    public IReadOnlyList<string> LevelSet { get; init; }

    // Time range. A relative preset chip ("24h") sets From only; when one is active it reads as a
    // single "time: last 24h" constraint instead of a bare From bound.
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public string TimePreset { get; init; }

    /// <summary>Fields beyond the built-in four, as (canonical field, substring value) in pane order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ExtraFields { get; init; }

    // The toolbar's rule-file view filter.
    public bool RuleFilterActive { get; init; }
    public int RuleFileCount { get; init; }

    /// <summary>Session quick rules, in apply order. Disabled ones are not constraints.</summary>
    public IReadOnlyList<QuickRuleView> QuickRules { get; init; }
}

/// <summary>
/// Which columns the filter pane can put a field filter on, and what to call them.
/// <para>
/// The list is exactly the text fields the SEARCH ENGINE can filter, not every column the grid shows:
/// four of them (Provider / TaskName / Message / Source) are first-class members of
/// <c>FilterSpec</c>, and the rest are applied as predicates ANDed into <c>FilterSpec.Query</c>, which
/// both backends already evaluate (SQLite via SQL, in-memory via the row getter). Time has the Time
/// section and Level the level chips, so neither appears here.
/// </para>
/// </summary>
public static class FilterFieldCatalog
{
    /// <summary>Canonical (LogQuery) name → the column label the pane shows, in menu order.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> All = new[]
    {
        new KeyValuePair<string, string>("provider", "Provider"),
        new KeyValuePair<string, string>("taskname", "TaskName"),
        new KeyValuePair<string, string>("message", "Message"),
        new KeyValuePair<string, string>("source", "Source"),
        new KeyValuePair<string, string>("processid", "ProcessId"),
        new KeyValuePair<string, string>("threadid", "ThreadId"),
        new KeyValuePair<string, string>("eventid", "EventId"),
        new KeyValuePair<string, string>("opcode", "OpCode"),
        new KeyValuePair<string, string>("channel", "Channel"),
        new KeyValuePair<string, string>("activityid", "ActivityId"),
        new KeyValuePair<string, string>("relatedactivityid", "RelatedActivityId"),
        new KeyValuePair<string, string>("machinename", "MachineName"),
        new KeyValuePair<string, string>("username", "Username"),
    };

    /// <summary>The four with a dedicated FilterSpec slot — and the only ones with "Pick from values"
    /// known-value dropdowns behind them. Shown by default.</summary>
    public static readonly IReadOnlyList<string> BuiltIn = new[] { "provider", "taskname", "message", "source" };

    public static bool IsBuiltIn(string canonical)
        => BuiltIn.Contains(canonical, StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string canonical)
        => All.Any(kv => string.Equals(kv.Key, canonical, StringComparison.OrdinalIgnoreCase));

    /// <summary>Column label for a canonical name (the canonical name itself if unrecognised).</summary>
    public static string DisplayOf(string canonical)
    {
        foreach (var kv in All)
            if (string.Equals(kv.Key, canonical, StringComparison.OrdinalIgnoreCase)) return kv.Value;
        return canonical ?? "";
    }
}

/// <summary>
/// The single source of truth for "what is narrowing this view". The pane renders this list as
/// removable pills AND takes the Filters badge count straight from <c>Count</c>, so the number on the
/// toolbar and the list in the pane cannot disagree (they used to be two hand-kept tallies).
/// </summary>
public static class ActiveFilterCatalog
{
    /// <summary>Values longer than this are ellipsized in a pill label.</summary>
    public const int MaxValueChars = 22;

    /// <summary>How many members of an OR-set are named before the label switches to "+N more".</summary>
    public const int MaxSetMembers = 3;

    public static string Ellipsize(string value, int max = MaxValueChars)
    {
        value = (value ?? "").Trim();
        if (max <= 1 || value.Length <= max) return value;
        return value.Substring(0, max - 1) + "…";
    }

    /// <summary>"(Error, Warning)" — at most <see cref="MaxSetMembers"/> named, then "+N more".</summary>
    public static string SetLabel(IReadOnlyList<string> values)
    {
        if (values == null || values.Count == 0) return "()";
        var shown = values.Take(MaxSetMembers).Select(v => Ellipsize(v, MaxValueChars));
        var s = string.Join(", ", shown);
        if (values.Count > MaxSetMembers) s += $", +{values.Count - MaxSetMembers} more";
        return "(" + s + ")";
    }

    private static string TimeLabel(DateTime t) => t.ToString("yyyy-MM-dd HH:mm");

    private static bool Has(string s) => !string.IsNullOrEmpty(s);
    private static bool Has(IReadOnlyList<string> s) => s != null && s.Count > 0;

    /// <summary>
    /// Every active constraint, in the pane's reading order: search, time, level, fields, rule files,
    /// quick rules. <paramref name="clearFor"/> supplies the per-pill clear action for a (kind, key);
    /// pass null in tests, where only labels and ordering matter.
    /// </summary>
    public static IReadOnlyList<ActiveFilter> Build(ActiveFilterState state, Func<string, string, Action> clearFor = null)
    {
        var list = new List<ActiveFilter>();
        if (state == null) return list;

        void Add(string kind, string key, string label)
            => list.Add(new ActiveFilter { Kind = kind, Key = key, Label = label, Clear = clearFor?.Invoke(kind, key) });

        if (Has(state.Search))
            Add("search", "", $"search: \"{Ellipsize(state.Search)}\"");

        // Time: a preset chip is one constraint ("last 24h"); an explicit range is up to two bounds,
        // each independently removable.
        if (Has(state.TimePreset))
            Add("time", "preset", $"time: last {state.TimePreset}");
        else
        {
            if (state.From.HasValue) Add("time", "from", $"time ≥ {TimeLabel(state.From.Value)}");
            if (state.To.HasValue) Add("time", "to", $"time ≤ {TimeLabel(state.To.Value)}");
        }

        if (Has(state.LevelSet)) Add("level", "set", $"level in {SetLabel(state.LevelSet)}");
        if (Has(state.Level)) Add("level", "", $"level = {Ellipsize(state.Level)}");

        void Field(string canonical, string substring, IReadOnlyList<string> set)
        {
            if (Has(set)) Add("field", canonical + ":set", $"{canonical} in {SetLabel(set)}");
            if (Has(substring)) Add("field", canonical, $"{canonical} ~ {Ellipsize(substring)}");
        }

        Field("provider", state.Provider, state.ProviderSet);
        Field("taskname", state.TaskName, state.TaskNameSet);
        Field("message", state.Message, null);
        Field("source", state.Source, state.SourceSet);

        if (state.ExtraFields != null)
            foreach (var kv in state.ExtraFields)
                if (Has(kv.Value)) Add("field", kv.Key, $"{kv.Key} ~ {Ellipsize(kv.Value)}");

        if (state.RuleFilterActive)
            Add("rules", "", $"rules: {state.RuleFileCount} {(state.RuleFileCount == 1 ? "file" : "files")}");

        if (state.QuickRules != null)
            for (int i = 0; i < state.QuickRules.Count; i++)
            {
                var qr = state.QuickRules[i];
                if (qr == null || !qr.Enabled) continue; // a disabled rule reshapes nothing
                Add("quickrule", i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    $"rule: {Ellipsize(qr.Label, MaxValueChars + 8)}");
            }

        return list;
    }
}
