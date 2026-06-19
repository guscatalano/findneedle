using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FindPluginCore.Searching.AutoRules;

/// <summary>
/// Pure evaluation of which auto-add rules apply to a given <see cref="AutoRuleContext"/>. No I/O and
/// no static state, so it's straightforward to unit-test. <see cref="AutoRulesStore"/> supplies the
/// entries and the global on/off switch; the resolver just matches conditions.
/// </summary>
public static class AutoRuleResolver
{
    /// <summary>
    /// Return the distinct rule-file paths of the enabled entries whose condition matches the context,
    /// preserving entry order. Built-in/user order is the caller's; duplicates (same path) collapse.
    /// </summary>
    public static List<string> Resolve(IEnumerable<AutoRuleEntry> entries, AutoRuleContext context)
    {
        var result = new List<string>();
        if (entries == null || context == null) return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (entry == null || !entry.Enabled || string.IsNullOrWhiteSpace(entry.RulePath)) continue;
            if (!Matches(entry.Condition, context)) continue;
            if (seen.Add(entry.RulePath)) result.Add(entry.RulePath);
        }
        return result;
    }

    /// <summary>True if every populated criterion matches. Always short-circuits; an all-empty,
    /// non-Always condition never matches.</summary>
    public static bool Matches(AutoRuleCondition c, AutoRuleContext ctx)
    {
        if (c == null) return false;
        if (c.Always) return true;

        bool any = false;

        if (c.Extensions is { Count: > 0 })
        {
            any = true;
            if (!ctx.Paths.Any(p => c.Extensions.Any(ext => HasExtension(p, ext)))) return false;
        }

        if (c.SourceTypes is { Count: > 0 })
        {
            any = true;
            if (!c.SourceTypes.Any(s => ctx.SourceTypes.Contains(s))) return false;
        }

        if (c.PathGlobs is { Count: > 0 })
        {
            any = true;
            if (!ctx.Paths.Any(p => c.PathGlobs.Any(g => GlobMatches(g, p)))) return false;
        }

        if (c.Providers is { Count: > 0 })
        {
            any = true;
            if (!c.Providers.Any(pr => ctx.Providers.Contains(pr))) return false;
        }

        if (c.MinBuild.HasValue || c.MaxBuild.HasValue)
        {
            any = true;
            if (!ctx.Build.HasValue) return false;
            if (c.MinBuild.HasValue && ctx.Build.Value < c.MinBuild.Value) return false;
            if (c.MaxBuild.HasValue && ctx.Build.Value > c.MaxBuild.Value) return false;
        }

        return any; // at least one criterion was specified and all specified ones passed
    }

    private static bool HasExtension(string path, string ext)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(ext)) return false;
        if (ext[0] != '.') ext = "." + ext;
        return path.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Match a path against a <c>*</c>/<c>?</c> glob, case-insensitive. <c>*</c> spans any run
    /// (including path separators) so <c>*panther*</c> works without the user writing <c>**</c>.</summary>
    public static bool GlobMatches(string glob, string path)
    {
        if (string.IsNullOrEmpty(glob)) return false;
        if (string.IsNullOrEmpty(path)) return false;
        // Normalize separators so a glob written with either slash matches either path style.
        glob = glob.Replace('\\', '/');
        path = path.Replace('\\', '/');
        var rx = "^" + Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(path, rx, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
