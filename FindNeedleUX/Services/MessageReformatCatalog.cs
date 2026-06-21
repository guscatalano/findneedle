using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FindNeedleUX.Services;

/// <summary>
/// One message-reformat rule: a regex (with named capture groups) that breaks a row's raw Message into
/// named fields for readable display. Fully generic — the named groups in <see cref="Pattern"/> become
/// the field labels. <see cref="Match"/> is an optional gate (substring/regex) deciding which rows the
/// rule applies to; if empty the rule applies to any row whose Message matches <see cref="Pattern"/>.
/// </summary>
public sealed class MessageReformatRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Match { get; set; } = "";   // optional gate (regex). Empty = no gate.
    public string Pattern { get; set; } = "";  // regex with (?<Name>...) groups => fields
    public bool BuiltIn { get; set; }
    public bool Enabled { get; set; } = true;
}

/// <summary>The result of reformatting one message: the rule that fired and the extracted fields.</summary>
public sealed class MessageReformatResult
{
    public string RuleName { get; set; } = "";
    public List<(string Field, string Value)> Fields { get; } = new();
}

/// <summary>
/// A catalog of message-reformat rules. Built-in examples (e.g. DISM) ship enabled; users can add their
/// own, disable built-ins, and reorder. Persisted to JSON under <c>%LocalAppData%\FindNeedle\</c>.
/// Reformatting is purely a display concern — it runs in the results viewer against the already-loaded
/// Message, so it works on any backend and needs no re-run of the search.
/// </summary>
public static class MessageReformatCatalog
{
    private static readonly string DefaultPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FindNeedle", "message-reformat.json");

    private static string _path = DefaultPath;

    internal static void SetStorageLocationForTests(string path) => _path = path;
    internal static void ResetStorageForTests() => _path = DefaultPath;

    public static event Action Changed;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>Shipped example rules. DISM is the flagship example — its Message packs timestamp,
    /// level, component, PID/TID, payload and function into one blob; this breaks it apart.</summary>
    public static readonly IReadOnlyList<MessageReformatRule> BuiltIns = new[]
    {
        new MessageReformatRule
        {
            Id = "builtin:dism",
            Name = "DISM log line",
            Description = "Break a DISM message into Timestamp / Level / Component / PID / TID / Payload / Function.",
            Match = @"\bDISM\b",
            Pattern = @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}),\s+(?<Level>\S+)\s+DISM\s+(?:(?<Component>[A-Za-z0-9 .]+?):\s+)?(?:PID=(?<PID>\d+)\s+TID=(?<TID>\d+)\s+)?(?<Payload>.*?)(?:\s+-\s+(?<Function>[A-Za-z0-9_:]+))?\s*$",
            BuiltIn = true,
        },
        new MessageReformatRule
        {
            Id = "builtin:cbs",
            Name = "CBS log line",
            Description = "Break a Windows servicing (CBS) message into Timestamp / Level / Component / Payload.",
            Match = @"\bCBS\b",
            Pattern = @"^(?<Timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}),\s+(?<Level>\S+)\s+CBS\s+(?:(?<Component>[A-Za-z0-9.]+):\s+)?(?<Payload>.*?)\s*$",
            BuiltIn = true,
        },
    };

    /// <summary>All rules (built-ins then user), ordered by saved order then name.</summary>
    public static List<MessageReformatRule> GetAll()
    {
        var data = Load();
        var disabled = new HashSet<string>(data.DisabledBuiltIns ?? new(), StringComparer.OrdinalIgnoreCase);

        var rules = new List<MessageReformatRule>();
        foreach (var b in BuiltIns)
        {
            var c = Clone(b);
            c.Enabled = !disabled.Contains(b.Id);
            rules.Add(c);
        }
        rules.AddRange(data.UserRules.Where(r => !r.BuiltIn));

        int Order(MessageReformatRule r) => data.Order != null && data.Order.TryGetValue(r.Id, out var o) ? o : int.MaxValue;
        return rules.OrderBy(Order).ThenBy(r => r.BuiltIn ? 0 : 1).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<MessageReformatRule> GetEnabled() => GetAll().Where(r => r.Enabled).ToList();

    public static MessageReformatRule GetById(string id) => GetAll().FirstOrDefault(r => r.Id == id);

    /// <summary>
    /// Apply the enabled rules to a message and return the first match's extracted fields, in regex
    /// group order. Returns null if no enabled rule applies. Empty captures are skipped so absent
    /// optional fields (e.g. a DISM line with no Component) don't clutter the display.
    /// </summary>
    public static MessageReformatResult Apply(string message)
    {
        if (string.IsNullOrEmpty(message)) return null;
        foreach (var rule in GetEnabled())
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern)) continue;
            if (!string.IsNullOrEmpty(rule.Match) && !SafeIsMatch(message, rule.Match)) continue;

            Match m;
            try { m = Regex.Match(message, rule.Pattern, RegexOptions.None, RegexTimeout); }
            catch { continue; }
            if (!m.Success) continue;

            var result = new MessageReformatResult { RuleName = rule.Name };
            var rx = new Regex(rule.Pattern);
            foreach (var name in rx.GetGroupNames())
            {
                if (int.TryParse(name, out _)) continue; // skip numbered groups
                var g = m.Groups[name];
                if (!g.Success) continue;
                var v = g.Value?.Trim();
                if (string.IsNullOrEmpty(v)) continue;
                result.Fields.Add((name, v));
            }
            if (result.Fields.Count > 0) return result;
        }
        return null;
    }

    private static bool SafeIsMatch(string input, string pattern)
    {
        try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase, RegexTimeout); }
        catch { return input.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0; }
    }

    /// <summary>Validate a pattern: must compile and contain at least one named group.</summary>
    public static bool TryValidatePattern(string pattern, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(pattern)) { error = "Pattern is required."; return false; }
        try
        {
            var rx = new Regex(pattern);
            if (!rx.GetGroupNames().Any(n => !int.TryParse(n, out _)))
            {
                error = "Pattern must contain at least one named group, e.g. (?<Field>...).";
                return false;
            }
            return true;
        }
        catch (Exception ex) { error = "Invalid regex: " + ex.Message; return false; }
    }

    public static void SetEnabled(string id, bool enabled)
    {
        var data = Load();
        if (id.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
        {
            data.DisabledBuiltIns ??= new();
            bool changed = enabled
                ? data.DisabledBuiltIns.RemoveAll(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)) > 0
                : (!data.DisabledBuiltIns.Contains(id, StringComparer.OrdinalIgnoreCase) && Add(data.DisabledBuiltIns, id));
            if (changed) { Save(data); Changed?.Invoke(); }
            return;
        }
        var u = data.UserRules.FirstOrDefault(r => r.Id == id);
        if (u == null || u.Enabled == enabled) return;
        u.Enabled = enabled;
        Save(data); Changed?.Invoke();
    }

    private static bool Add(List<string> list, string id) { list.Add(id); return true; }

    public static MessageReformatRule Upsert(MessageReformatRule rule)
    {
        if (rule == null) return null;
        rule.BuiltIn = false;
        if (string.IsNullOrEmpty(rule.Id) || rule.Id.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
            rule.Id = Guid.NewGuid().ToString("N");

        var data = Load();
        var idx = data.UserRules.FindIndex(r => r.Id == rule.Id);
        if (idx >= 0) data.UserRules[idx] = rule; else data.UserRules.Add(rule);
        Save(data); Changed?.Invoke();
        return rule;
    }

    public static void Remove(string id)
    {
        var data = Load();
        if (data.UserRules.RemoveAll(r => r.Id == id) > 0) { Save(data); Changed?.Invoke(); }
    }

    public static void Move(string id, int delta)
    {
        var all = GetAll();
        int i = all.FindIndex(r => r.Id == id);
        int t = i + delta;
        if (i < 0 || t < 0 || t >= all.Count) return;
        (all[i], all[t]) = (all[t], all[i]);
        var data = Load();
        data.Order ??= new();
        for (int k = 0; k < all.Count; k++) data.Order[all[k].Id] = k;
        Save(data); Changed?.Invoke();
    }

    private static MessageReformatRule Clone(MessageReformatRule r) => new()
    {
        Id = r.Id, Name = r.Name, Description = r.Description, Match = r.Match, Pattern = r.Pattern,
        BuiltIn = r.BuiltIn, Enabled = r.Enabled,
    };

    private static Data Load()
    {
        try
        {
            if (!File.Exists(_path)) return new Data();
            return JsonSerializer.Deserialize<Data>(File.ReadAllText(_path)) ?? new Data();
        }
        catch { return new Data(); }
    }

    private static void Save(Data data)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"MessageReformatCatalog.Save failed: {ex.Message}"); }
    }

    private sealed class Data
    {
        public List<MessageReformatRule> UserRules { get; set; } = new();
        public List<string> DisabledBuiltIns { get; set; } = new();
        public Dictionary<string, int> Order { get; set; } = new();
    }
}
