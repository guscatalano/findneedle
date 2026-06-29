using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FindNeedlePluginLib;

namespace FindNeedleRuleDSL;

/// <summary>
/// Compiles RuleDSL <c>purpose: "scope"</c> sections into a cheap, decode-time <see cref="DecodeScope"/>
/// (and validates them). A scope rule is the "applies before everything" rule: it filters the load at decode
/// time, so it may only reference fields available BEFORE the wrap — provider, time, level — never message or
/// extracted fields (computing those is the wrap we're skipping). See docs/scope-rule-design.md.
/// </summary>
public static class ScopeRuleParser
{
    private const string ScopeActionType = "scope";

    /// <summary>Validate scope sections are pushdownable; returns human-readable errors (empty = ok).</summary>
    public static IReadOnlyList<string> Validate(IEnumerable<UnifiedRuleSection>? sections)
    {
        var errors = new List<string>();
        if (sections == null) return errors;
        foreach (var s in sections)
        {
            foreach (var r in s.Rules ?? new List<UnifiedRule>())
            {
                if (!string.IsNullOrEmpty(r.Match) || !string.IsNullOrEmpty(r.Unmatch))
                    errors.Add($"scope section '{s.Name}', rule '{r.Name}': a scope rule cannot test message/fields " +
                               "(match/unmatch) — only providers, time, and level are available before decode.");
                var a = r.Action;
                if (a == null) continue;
                if (!string.IsNullOrEmpty(a.TimeFrom) && !TryParseUtc(a.TimeFrom, out _))
                    errors.Add($"scope section '{s.Name}': timeFrom '{a.TimeFrom}' is not a valid ISO-8601 timestamp.");
                if (!string.IsNullOrEmpty(a.TimeTo) && !TryParseUtc(a.TimeTo, out _))
                    errors.Add($"scope section '{s.Name}': timeTo '{a.TimeTo}' is not a valid ISO-8601 timestamp.");
                if (a.Levels != null)
                    foreach (var lvl in a.Levels)
                        if (!Enum.TryParse<Level>(lvl, ignoreCase: true, out _))
                            errors.Add($"scope section '{s.Name}': unknown level '{lvl}'.");
                if (!string.IsNullOrEmpty(a.ProviderMode)
                    && !a.ProviderMode.Equals("include", StringComparison.OrdinalIgnoreCase)
                    && !a.ProviderMode.Equals("exclude", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"scope section '{s.Name}': providerMode must be 'include' or 'exclude', got '{a.ProviderMode}'.");
            }
        }
        return errors;
    }

    /// <summary>Compile scope sections into a single DecodeScope (AND-combined across sections). Null when
    /// there's no effective scope (load everything). Assumes the sections passed Validate.</summary>
    public static DecodeScope? Build(IEnumerable<UnifiedRuleSection>? sections)
    {
        if (sections == null) return null;
        HashSet<string>? include = null, exclude = null;
        HashSet<int>? levels = null;
        DateTime? from = null, to = null;
        bool any = false;

        foreach (var s in sections)
        {
            var act = s.Rules?.FirstOrDefault(r => string.Equals(r.Action?.Type, ScopeActionType, StringComparison.OrdinalIgnoreCase))?.Action
                      ?? s.Rules?.FirstOrDefault()?.Action;
            bool excludeMode = act?.ProviderMode != null && act.ProviderMode.Equals("exclude", StringComparison.OrdinalIgnoreCase);

            if (s.Providers != null && s.Providers.Count > 0)
            {
                any = true;
                if (excludeMode)
                {
                    exclude ??= new(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in s.Providers) exclude.Add(p);
                }
                else
                {
                    var inc = new HashSet<string>(s.Providers, StringComparer.OrdinalIgnoreCase);
                    include = include == null ? inc : Intersect<string>(include, inc); // AND across sections
                }
            }

            if (act != null)
            {
                if (TryParseUtc(act.TimeFrom, out var f)) { any = true; from = from == null ? f : (f > from ? f : from); }
                if (TryParseUtc(act.TimeTo, out var t)) { any = true; to = to == null ? t : (t < to ? t : to); }
                if (act.Levels != null && act.Levels.Count > 0)
                {
                    any = true;
                    var ls = new HashSet<int>();
                    foreach (var name in act.Levels)
                        if (Enum.TryParse<Level>(name, ignoreCase: true, out var lv)) ls.Add((int)lv);
                    levels = levels == null ? ls : Intersect<int>(levels, ls);
                }
            }
        }

        if (!any) return null;
        var scope = new DecodeScope
        {
            IncludeProviders = include,
            ExcludeProviders = exclude,
            FromUtc = from,
            ToUtc = to,
            Levels = levels,
        };
        return scope.IsEmpty ? null : scope;
    }

    /// <summary>Generate a `scope` rule set from a triage selection (the panel calls this, then writes it as a
    /// *.rules.json and adds it to the search's RulesConfigPaths). Round-trips through Build/Validate.</summary>
    public static UnifiedRuleSet BuildScopeRuleSet(
        IEnumerable<string> providers, bool exclude = false,
        DateTime? fromUtc = null, DateTime? toUtc = null, IEnumerable<string>? levels = null)
    {
        var section = new UnifiedRuleSection
        {
            Name = "triage scope",
            Purpose = "scope",
            Providers = providers?.ToList() ?? new List<string>(),
            Rules = new List<UnifiedRule>
            {
                new UnifiedRule
                {
                    Name = "scope",
                    Action = new UnifiedRuleAction
                    {
                        Type = "scope",
                        ProviderMode = exclude ? "exclude" : "include",
                        TimeFrom = fromUtc?.ToUniversalTime().ToString("o"),
                        TimeTo = toUtc?.ToUniversalTime().ToString("o"),
                        Levels = levels?.ToList(),
                    },
                },
            },
        };
        return new UnifiedRuleSet { Title = "Triage scope", Sections = new List<UnifiedRuleSection> { section } };
    }

    /// <summary>Serialize a scope rule set to a *.rules.json the search engine can load.</summary>
    public static string ToJson(UnifiedRuleSet ruleSet) =>
        System.Text.Json.JsonSerializer.Serialize(ruleSet, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

    private static HashSet<T> Intersect<T>(HashSet<T> a, HashSet<T> b)
    {
        var r = new HashSet<T>(a, a.Comparer);
        r.IntersectWith(b);
        return r;
    }

    private static bool TryParseUtc(string? s, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrEmpty(s)) return false;
        if (!DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt)) return false;
        utc = dt;
        return true;
    }
}
