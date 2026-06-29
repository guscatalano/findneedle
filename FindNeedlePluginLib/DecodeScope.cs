using System;
using System.Collections.Generic;

namespace FindNeedlePluginLib;

/// <summary>
/// A compiled, O(1) "what to load" predicate evaluated by a format decoder BEFORE it wraps a raw event into
/// an <see cref="ISearchResult"/> — the cheap part of a `scope` RuleDSL rule (see docs/scope-rule-design.md).
/// It can only test what's available pre-wrap (provider, timestamp, level), because computing Message/payload
/// IS the wrap we're trying to skip. Events that don't pass are never wrapped, stored, or indexed.
///
/// Null = no scope (load everything). Built from RuleDSL `scope` sections by the search pipeline and handed
/// to the decoders for the duration of a load.
/// </summary>
public sealed class DecodeScope
{
    /// <summary>Ambient scope for the current load, set by the search pipeline and read by format decoders
    /// (they don't reference the core, so this shared static is how the scope reaches them). Null = load all.
    /// PROTOTYPE: process-global; production would thread it per-search (see docs/scope-rule-design.md §8).</summary>
    public static DecodeScope? Current;

    /// <summary>Allow-list: if non-empty, only these providers pass (case-insensitive).</summary>
    public HashSet<string>? IncludeProviders { get; init; }
    /// <summary>Drop-list: these providers never pass (e.g. the kernel firehose). Applied after include.</summary>
    public HashSet<string>? ExcludeProviders { get; init; }
    /// <summary>Inclusive lower/upper time bounds (UTC); null = open-ended.</summary>
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    /// <summary>If non-empty, only these levels pass ((int)Level). A level &lt; 0 means "unknown" and is
    /// not filtered (a decoder that can't cheaply get the level passes -1).</summary>
    public HashSet<int>? Levels { get; init; }

    /// <summary>True when this scope keeps nothing meaningful (all predicates empty) — caller can treat as null.</summary>
    public bool IsEmpty =>
        (IncludeProviders == null || IncludeProviders.Count == 0)
        && (ExcludeProviders == null || ExcludeProviders.Count == 0)
        && FromUtc == null && ToUtc == null
        && (Levels == null || Levels.Count == 0);

    /// <summary>O(1) keep test, called per raw decoded event (tens of millions) — hash lookups + compares only.
    /// Each argument is "unknown" when the decoder can't cheaply supply it, and an unknown dimension is NOT
    /// filtered (so a format applies only the dimensions it actually has): a <c>null</c> <paramref name="provider"/>
    /// skips the provider lists (plain text has no provider), a <c>null</c> <paramref name="tsUtc"/> skips the time
    /// window (an un-timestamped line), and a <paramref name="level"/> &lt; 0 skips the level set.</summary>
    public bool Keep(string? provider, DateTime? tsUtc, int level)
    {
        if (provider != null && IncludeProviders != null && IncludeProviders.Count > 0 && !IncludeProviders.Contains(provider)) return false;
        if (provider != null && ExcludeProviders != null && ExcludeProviders.Count > 0 && ExcludeProviders.Contains(provider)) return false;
        if (tsUtc.HasValue && FromUtc.HasValue && tsUtc.Value < FromUtc.Value) return false;
        if (tsUtc.HasValue && ToUtc.HasValue && tsUtc.Value > ToUtc.Value) return false;
        if (Levels != null && Levels.Count > 0 && level >= 0 && !Levels.Contains(level)) return false;
        return true;
    }
}
