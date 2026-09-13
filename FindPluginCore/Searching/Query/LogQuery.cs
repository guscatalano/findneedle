using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FindPluginCore.Searching.Query;

/// <summary>
/// A tiny query language for the result viewer's search box. Lets the user write field-scoped
/// predicates with boolean logic, e.g.:
///   <code>msg != "this" AND taskname == "this"</code>
///   <code>level == Error OR (provider ~ Kernel AND NOT msg ~ debug)</code>
/// A bare term with no field/operator (e.g. <c>timeout</c>) stays a substring-across-all-columns
/// search, so existing simple usage is unchanged.
///
/// The parsed tree compiles two ways from the SAME AST so the in-memory and SQLite backends can't
/// drift: <see cref="QueryNode.Evaluate"/> (row predicate) and <see cref="QueryNode.AppendSql"/>
/// (parameterized SQL WHERE fragment).
/// </summary>
public enum QueryOp { Eq, Ne, Contains, NotContains, Gt, Lt, Ge, Le, Regex }

/// <summary>How a field's value is stored / compared (Level is an int enum in SQL; Time is an ISO string;
/// Tag lives in the viewer, not the store; Data is a key inside the StructuredData JSON).</summary>
public enum FieldKind { Text, Level, Time, Tag, Data }

public abstract class QueryNode
{
    /// <summary>Evaluate against a row. <paramref name="get"/> returns a row field by canonical name
    /// (see <see cref="LogQuery.Fields"/>); the special name <c>"*"</c> returns all searchable text.</summary>
    public abstract bool Evaluate(Func<string, string> get);

    /// <summary>Append a parameterized SQL boolean expression for this node; returns the fragment.</summary>
    public abstract string AppendSql(QuerySqlContext ctx);
}

public sealed class AndNode : QueryNode
{
    public QueryNode L, R;
    public AndNode(QueryNode l, QueryNode r) { L = l; R = r; }
    public override bool Evaluate(Func<string, string> g) => L.Evaluate(g) && R.Evaluate(g);
    public override string AppendSql(QuerySqlContext c) => $"({L.AppendSql(c)} AND {R.AppendSql(c)})";
}

public sealed class OrNode : QueryNode
{
    public QueryNode L, R;
    public OrNode(QueryNode l, QueryNode r) { L = l; R = r; }
    public override bool Evaluate(Func<string, string> g) => L.Evaluate(g) || R.Evaluate(g);
    public override string AppendSql(QuerySqlContext c) => $"({L.AppendSql(c)} OR {R.AppendSql(c)})";
}

public sealed class NotNode : QueryNode
{
    public QueryNode Inner;
    public NotNode(QueryNode inner) { Inner = inner; }
    public override bool Evaluate(Func<string, string> g) => !Inner.Evaluate(g);
    public override string AppendSql(QuerySqlContext c) => $"(NOT {Inner.AppendSql(c)})";
}

/// <summary>A bare term: substring match across all searchable columns (the classic global search).</summary>
public sealed class AnyContainsNode : QueryNode
{
    public string Value;
    public AnyContainsNode(string v) { Value = v ?? ""; }
    public override bool Evaluate(Func<string, string> g)
        => (g("*") ?? "").IndexOf(Value, StringComparison.OrdinalIgnoreCase) >= 0;
    public override string AppendSql(QuerySqlContext c) => c.AnyContainsSql(Value);
}

public sealed class PredicateNode : QueryNode
{
    public string Field;   // canonical name (lowercase), already validated
    public QueryOp Op;
    public string Value;

    public PredicateNode(string field, QueryOp op, string value) { Field = field; Op = op; Value = value ?? ""; }

    public override bool Evaluate(Func<string, string> g)
    {
        if (LogQuery.KindOf(Field) == FieldKind.Tag)
        {
            // Tags are viewer state keyed by the row's stable id; the row exposes that id as "rowid".
            long.TryParse(g("rowid") ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out var id);
            return LogQuery.TagMatches(id, Op, Value);
        }
        return Matches(g(Field) ?? "", Op, Value, LogQuery.KindOf(Field));
    }

    /// <summary>The one comparison every backend uses (in-memory rows, tag lookups).</summary>
    internal static bool Matches(string actual, QueryOp op, string value, FieldKind kind)
    {
        switch (op)
        {
            case QueryOp.Eq:          return string.Equals(actual, value, StringComparison.OrdinalIgnoreCase);
            case QueryOp.Ne:          return !string.Equals(actual, value, StringComparison.OrdinalIgnoreCase);
            case QueryOp.Contains:    return actual.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
            case QueryOp.NotContains: return actual.IndexOf(value, StringComparison.OrdinalIgnoreCase) < 0;
            case QueryOp.Regex:       return LogQuery.RegexMatches(value, actual);
            default:                  return CompareOrdered(actual, op, value, kind);
        }
    }

    private static bool CompareOrdered(string actual, QueryOp op, string value, FieldKind kind)
    {
        int cmp;
        var av = LogQuery.TryParseTime(actual);
        var bv = LogQuery.TryParseTime(value);
        if (kind == FieldKind.Time && av.HasValue && bv.HasValue)
            cmp = DateTime.Compare(av.Value, bv.Value);
        else if (double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var an)
                 && double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var bn))
            cmp = an.CompareTo(bn);
        else
            cmp = string.Compare(actual, value, StringComparison.OrdinalIgnoreCase);

        return op switch
        {
            QueryOp.Gt => cmp > 0,
            QueryOp.Lt => cmp < 0,
            QueryOp.Ge => cmp >= 0,
            QueryOp.Le => cmp <= 0,
            _ => false,
        };
    }

    public override string AppendSql(QuerySqlContext c) => c.PredicateSql(Field, Op, Value);
}

/// <summary>Accumulates the parameterized SQL for a query and knows how each field maps to a column.</summary>
public sealed class QuerySqlContext
{
    public readonly List<KeyValuePair<string, object>> Parameters = new();
    private int _i;

    private string Add(object value)
    {
        var name = "@q" + (_i++);
        Parameters.Add(new KeyValuePair<string, object>(name, value));
        return name;
    }

    private static string EscapeLike(string s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>SQL for a single field predicate (delegates Level → int, Time → ISO compare).</summary>
    public string PredicateSql(string field, QueryOp op, string value)
    {
        var (col, kind) = LogQuery.ColumnOf(field);

        if (kind == FieldKind.Tag)
        {
            // Tags are not in the store. Decide per tagged row with the same comparison the in-memory
            // path uses, then express the answer as an Id list. Untagged rows compare as "" (so
            // `tag != Important` and `NOT tag ~ x` include them, `tag == Important` does not).
            var tags = LogQuery.TagSnapshot?.Invoke();
            bool untaggedMatch = LogQuery.TagMatchesText("", "", op, value);
            var matched = new List<long>(); var unmatched = new List<long>();
            if (tags != null)
                foreach (var kv in tags)
                    (LogQuery.TagMatchesText(kv.Value.Name, kv.Value.Text, op, value) ? matched : unmatched).Add(kv.Key);
            var list = untaggedMatch ? unmatched : matched;
            if (list.Count == 0) return untaggedMatch ? "1=1" : "1=0";
            var sb = new StringBuilder(untaggedMatch ? "(Id NOT IN (" : "(Id IN (");
            for (int i = 0; i < list.Count; i++) { if (i > 0) sb.Append(','); sb.Append(list[i].ToString(CultureInfo.InvariantCulture)); }
            return sb.Append("))").ToString();
        }

        if (op == QueryOp.Regex)
            return $"{col} REGEXP {Add(value)}"; // REGEXP is registered on the connection (SqliteStorage)

        if (kind == FieldKind.Level && (op == QueryOp.Eq || op == QueryOp.Ne))
        {
            // Level is stored as the int enum value; map the name (Error/Warning/…) to that int.
            int lvl = LogQuery.LevelToInt(value);
            var p = Add(lvl);
            return op == QueryOp.Eq ? $"{col} = {p}" : $"({col} <> {p} OR {col} IS NULL)";
        }

        switch (op)
        {
            case QueryOp.Eq:
                return $"{col} = {Add(value)} COLLATE NOCASE";
            case QueryOp.Ne:
                return $"({col} <> {Add(value)} COLLATE NOCASE OR {col} IS NULL)";
            case QueryOp.Contains:
                return $"{col} LIKE {Add("%" + EscapeLike(value) + "%")} ESCAPE '\\'";
            case QueryOp.NotContains:
                return $"({col} NOT LIKE {Add("%" + EscapeLike(value) + "%")} ESCAPE '\\' OR {col} IS NULL)";
            default:
                // Ordered comparison. Time compares the ISO string (lexical == chronological); others as text.
                string v = kind == FieldKind.Time ? LogQuery.NormalizeTime(value) : value;
                string sqlOp = op switch { QueryOp.Gt => ">", QueryOp.Lt => "<", QueryOp.Ge => ">=", QueryOp.Le => "<=", _ => "=" };
                return $"{col} {sqlOp} {Add(v)}";
        }
    }

    /// <summary>SQL for a bare term: substring across the common text columns (matches the in-memory "*").</summary>
    public string AnyContainsSql(string value)
    {
        var p = Add("%" + EscapeLike(value) + "%");
        return "(Source LIKE " + p + " ESCAPE '\\' OR TaskName LIKE " + p + " ESCAPE '\\' OR " +
               "Message LIKE " + p + " ESCAPE '\\' OR ResultSource LIKE " + p + " ESCAPE '\\' OR " +
               "SearchableData LIKE " + p + " ESCAPE '\\' OR LogTime LIKE " + p + " ESCAPE '\\')";
    }
}

public static class LogQuery
{
    /// <summary>Canonical field name → (SQL column, kind). Viewer "Provider" is SQL "Source"; viewer
    /// "Source" is SQL "ResultSource" (the usual naming gotcha).</summary>
    private static readonly Dictionary<string, (string col, FieldKind kind)> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["message"] = ("Message", FieldKind.Text),
            ["taskname"] = ("TaskName", FieldKind.Text),
            ["provider"] = ("Source", FieldKind.Text),
            ["source"] = ("ResultSource", FieldKind.Text),
            ["level"] = ("Level", FieldKind.Level),
            ["processid"] = ("ProcessId", FieldKind.Text),
            ["threadid"] = ("ThreadId", FieldKind.Text),
            ["activityid"] = ("ActivityId", FieldKind.Text),
            ["relatedactivityid"] = ("RelatedActivityId", FieldKind.Text),
            ["eventid"] = ("EventId", FieldKind.Text),
            ["channel"] = ("Channel", FieldKind.Text),
            ["machinename"] = ("MachineName", FieldKind.Text),
            ["username"] = ("Username", FieldKind.Text),
            ["opcode"] = ("OpCode", FieldKind.Text),
            ["time"] = ("LogTime", FieldKind.Time),
            ["rawlevel"] = ("RawLevel", FieldKind.Text),
            ["tag"] = ("Id", FieldKind.Tag),
        };

    /// <summary>Prefix for structured-payload fields: <c>data.ProcessId == 4</c> reads the ProcessId key of
    /// the row's StructuredData JSON (json_extract in SQLite, a dictionary lookup in memory).</summary>
    public const string DataPrefix = "data.";

    private static bool IsDataField(string name)
        => name != null && name.StartsWith(DataPrefix, StringComparison.OrdinalIgnoreCase) && name.Length > DataPrefix.Length;

    /// <summary>A data key is spliced into the SQL path expression, so only plain identifier characters are allowed.</summary>
    private static bool IsSafeDataKey(string key)
    {
        foreach (var ch in key) if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')) return false;
        return key.Length > 0;
    }

    /// <summary>Aliases the user can type → canonical field name.</summary>
    private static readonly Dictionary<string, string> Alias =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["msg"] = "message", ["message"] = "message",
            ["task"] = "taskname", ["taskname"] = "taskname",
            ["provider"] = "provider", ["prov"] = "provider",
            ["source"] = "source", ["src"] = "source",
            ["level"] = "level", ["lvl"] = "level",
            ["pid"] = "processid", ["processid"] = "processid",
            ["tid"] = "threadid", ["threadid"] = "threadid",
            ["activityid"] = "activityid", ["aid"] = "activityid", ["activity"] = "activityid",
            ["relatedactivityid"] = "relatedactivityid", ["raid"] = "relatedactivityid", ["related"] = "relatedactivityid",
            ["eventid"] = "eventid", ["event"] = "eventid", ["id"] = "eventid",
            ["channel"] = "channel",
            ["machine"] = "machinename", ["machinename"] = "machinename", ["computer"] = "machinename",
            ["user"] = "username", ["username"] = "username",
            ["opcode"] = "opcode",
            ["time"] = "time", ["timestamp"] = "time",
            ["rawlevel"] = "rawlevel", ["raw"] = "rawlevel",
            ["tag"] = "tag", ["tags"] = "tag",
        };

    /// <summary>Supplies the viewer's row tags (stable row id → name + note) so `tag` predicates can be
    /// answered in both backends. Null when no viewer is attached.</summary>
    public static Func<IReadOnlyDictionary<long, (string Name, string Text)>> TagSnapshot { get; set; }

    /// <summary>The date a date-less time (<c>time ~ 12:34:56</c>) is resolved against - the loaded data's
    /// day, set by the viewer. Null falls back to today.</summary>
    public static DateTime? DefaultDate { get; set; }

    internal static bool TagMatches(long rowId, QueryOp op, string value)
    {
        var tags = TagSnapshot?.Invoke();
        if (tags != null && tags.TryGetValue(rowId, out var t)) return TagMatchesText(t.Name, t.Text, op, value);
        return TagMatchesText("", "", op, value);
    }

    /// <summary>Eq/Ne compare the tag NAME (Important, Question, …); the substring / regex forms search the
    /// name and the note together, so <c>tag ~ leak</c> finds a note that mentions one.</summary>
    internal static bool TagMatchesText(string name, string text, QueryOp op, string value)
        => op == QueryOp.Eq || op == QueryOp.Ne
            ? PredicateNode.Matches(name ?? "", op, value, FieldKind.Text)
            : PredicateNode.Matches(((name ?? "") + " " + (text ?? "")).Trim(), op, value, FieldKind.Text);

    private static readonly Dictionary<string, System.Text.RegularExpressions.Regex> RegexCache = new(StringComparer.Ordinal);

    /// <summary>Case-insensitive regex match with a bounded backtracking budget, shared by the in-memory
    /// path and the SQLite REGEXP function.</summary>
    public static bool RegexMatches(string pattern, string input)
    {
        if (pattern == null) return false;
        System.Text.RegularExpressions.Regex rx;
        lock (RegexCache)
        {
            if (!RegexCache.TryGetValue(pattern, out rx))
            {
                try
                {
                    rx = new System.Text.RegularExpressions.Regex(pattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                        TimeSpan.FromMilliseconds(200));
                }
                catch (ArgumentException) { rx = null; }
                if (RegexCache.Count > 64) RegexCache.Clear();
                RegexCache[pattern] = rx;
            }
        }
        if (rx == null) return false;
        try { return rx.IsMatch(input ?? ""); }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException) { return false; }
    }

    /// <summary>Validate a regex pattern up front so a typo is a parse error, not a silently-empty result.</summary>
    public static string RegexError(string pattern)
    {
        try { _ = new System.Text.RegularExpressions.Regex(pattern ?? ""); return null; }
        catch (ArgumentException ex) { return ex.Message; }
    }

    /// <summary>The canonical fields (for the UI help text).</summary>
    public static IEnumerable<string> Fields => Map.Keys;

    public static bool IsField(string name) => name != null && (Alias.ContainsKey(name) || IsDataField(name));
    public static string Canonical(string name)
        => Alias.TryGetValue(name ?? "", out var c) ? c
         : IsDataField(name) && IsSafeDataKey(name.Substring(DataPrefix.Length)) ? DataPrefix + name.Substring(DataPrefix.Length)
         : null;
    public static (string col, FieldKind kind) ColumnOf(string canonical)
        => IsDataField(canonical)
            ? ($"json_extract(StructuredData, '$.{canonical.Substring(DataPrefix.Length)}')", FieldKind.Data)
            : Map[canonical];
    public static FieldKind KindOf(string canonical)
        => IsDataField(canonical) ? FieldKind.Data : Map.TryGetValue(canonical ?? "", out var v) ? v.kind : FieldKind.Text;

    /// <summary>The operators, for completion and help.</summary>
    public static readonly IReadOnlyList<(string Op, string Meaning)> Operators = new[]
    {
        ("==", "equals"), ("!=", "not equal"), ("~", "contains"), ("!~", "does not contain"),
        ("=~", "matches regex"), (">", "greater"), ("<", "less"), (">=", "at least"), ("<=", "at most"),
    };

    public static int LevelToInt(string name)
    {
        // Mirror FindNeedlePluginLib.Level ordering without taking the dependency here.
        return (name ?? "").Trim().ToLowerInvariant() switch
        {
            "catastrophic" or "critical" or "fatal" => 0,
            "error" => 1,
            "warning" or "warn" => 2,
            "info" or "information" => 3,
            "verbose" or "debug" => 4,
            _ => -1,
        };
    }

    /// <summary>Parse a timestamp flexibly (invariant culture, accepts ISO round-trip and plain forms).</summary>
    public static DateTime? TryParseTime(string s)
    {
        if (!DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault, out var dt))
            return null;
        // "12:34:56" alone parses onto year 1: a time of day, not a date. Put it on the data's day.
        if (dt.Year == 1 && !string.IsNullOrEmpty(s) && s.Trim().IndexOf('-') < 0 && s.Trim().IndexOf('/') < 0)
        {
            var day = (DefaultDate ?? DateTime.Today).Date;
            dt = day + dt.TimeOfDay;
        }
        return dt;
    }

    /// <summary>Parse a window like "2s", "500ms", "1m", "1h", "±2s", "+-2s".</summary>
    public static bool TryParseWindow(string s, out TimeSpan window)
    {
        window = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var t = s.Trim().TrimStart('±').TrimStart('+').TrimStart('-').TrimStart('/');
        if (t.StartsWith("-")) t = t.Substring(1); // "+-2s"
        int i = 0; while (i < t.Length && (char.IsDigit(t[i]) || t[i] == '.')) i++;
        if (i == 0 || !double.TryParse(t.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return false;
        switch (t.Substring(i).ToLowerInvariant())
        {
            case "ms": window = TimeSpan.FromMilliseconds(n); return true;
            case "s": case "sec": case "": window = TimeSpan.FromSeconds(n); return true;
            case "m": case "min": window = TimeSpan.FromMinutes(n); return true;
            case "h": window = TimeSpan.FromHours(n); return true;
            default: return false;
        }
    }

    /// <summary>The range query for "around this time": <c>time >= from AND time <= to</c>.</summary>
    public static QueryNode AroundTime(DateTime center, TimeSpan window)
        => new AndNode(new PredicateNode("time", QueryOp.Ge, (center - window).ToString("o")),
                       new PredicateNode("time", QueryOp.Le, (center + window).ToString("o")));

    /// <summary>The query TEXT for "around this time", the way a user would type it.</summary>
    public static string AroundTimeText(DateTime center, TimeSpan window)
    {
        string w = window.TotalHours >= 1 && window.TotalHours == Math.Floor(window.TotalHours) ? $"{window.TotalHours:0}h"
                 : window.TotalMinutes >= 1 && window.TotalMinutes == Math.Floor(window.TotalMinutes) ? $"{window.TotalMinutes:0}m"
                 : window.TotalSeconds >= 1 && window.TotalSeconds == Math.Floor(window.TotalSeconds) ? $"{window.TotalSeconds:0}s"
                 : $"{window.TotalMilliseconds:0}ms";
        return $"time ~ \"{center:yyyy-MM-dd HH:mm:ss.fff}\" ±{w}";
    }

    public static string NormalizeTime(string value)
    {
        var t = TryParseTime(value);
        return t?.ToString("o") ?? (value ?? "");
    }

    /// <summary>True if the text looks like a structured query (has a field operator or a top-level
    /// boolean keyword) rather than a plain substring. Used for auto-detection in the search box.</summary>
    public static bool LooksStructured(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try
        {
            var toks = Tokenize(text);
            for (int i = 0; i < toks.Count; i++)
            {
                if (toks[i].Type == TokType.Op) return true;
                if (toks[i].Type == TokType.Word &&
                    (toks[i].Text.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
                     toks[i].Text.Equals("OR", StringComparison.OrdinalIgnoreCase) ||
                     toks[i].Text.Equals("NOT", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch { return false; }
        return false;
    }

    /// <summary>Parse text into a query tree. Returns false (with <paramref name="error"/>) on a syntax
    /// error. A plain (non-structured) term parses to a single <see cref="AnyContainsNode"/>.</summary>
    public static bool TryParse(string text, out QueryNode node, out string error)
    {
        node = null; error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "empty query"; return false; }
        try
        {
            var p = new Parser(Tokenize(text));
            node = p.ParseOr();
            p.ExpectEnd();
            return true;
        }
        catch (QueryParseException ex) { error = ex.Message; node = null; return false; }
    }

    // ----- tokenizer -----
    internal enum TokType { Word, String, Op, LParen, RParen, End }
    internal readonly struct Tok
    {
        public readonly TokType Type; public readonly string Text; public readonly QueryOp Op;
        public Tok(TokType t, string s, QueryOp op = QueryOp.Eq) { Type = t; Text = s; Op = op; }
    }

    internal static List<Tok> Tokenize(string s)
    {
        var toks = new List<Tok>();
        int i = 0, n = s.Length;
        while (i < n)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '(') { toks.Add(new Tok(TokType.LParen, "(")); i++; continue; }
            if (c == ')') { toks.Add(new Tok(TokType.RParen, ")")); i++; continue; }
            if (c == '"' || c == '\'')
            {
                char q = c; i++; var sb = new StringBuilder();
                // Only the quote itself and a backslash are escapable; any other backslash stays literal
                // so regex classes (\s, \d) and Windows paths survive without doubling.
                while (i < n && s[i] != q)
                {
                    if (s[i] == '\\' && i + 1 < n && (s[i + 1] == q || s[i + 1] == '\\')) i++;
                    sb.Append(s[i++]);
                }
                if (i >= n) throw new QueryParseException("unterminated string");
                i++; toks.Add(new Tok(TokType.String, sb.ToString())); continue;
            }
            // operators: != !~ == ~ = >= <= > <
            if (TryOp(s, ref i, out var op, out var opLen)) { toks.Add(new Tok(TokType.Op, s.Substring(i - opLen, opLen), op)); continue; }
            // word: field name, value, or boolean keyword
            int start = i;
            while (i < n && !char.IsWhiteSpace(s[i]) && s[i] != '(' && s[i] != ')' && !IsOpStart(s, i)) i++;
            if (i == start) throw new QueryParseException($"unexpected character '{s[start]}'");
            toks.Add(new Tok(TokType.Word, s.Substring(start, i - start)));
        }
        toks.Add(new Tok(TokType.End, ""));
        return toks;
    }

    private static bool IsOpStart(string s, int i)
    {
        char c = s[i];
        return c == '=' || c == '!' || c == '~' || c == '>' || c == '<';
    }

    private static bool TryOp(string s, ref int i, out QueryOp op, out int len)
    {
        op = QueryOp.Eq; len = 0;
        string two = i + 1 < s.Length ? s.Substring(i, 2) : "";
        switch (two)
        {
            case "==": op = QueryOp.Eq; len = 2; break;
            case "=~": op = QueryOp.Regex; len = 2; break;
            case "!=": op = QueryOp.Ne; len = 2; break;
            case "!~": op = QueryOp.NotContains; len = 2; break;
            case ">=": op = QueryOp.Ge; len = 2; break;
            case "<=": op = QueryOp.Le; len = 2; break;
        }
        if (len == 0)
        {
            switch (s[i])
            {
                case '=': op = QueryOp.Eq; len = 1; break;
                case '~': op = QueryOp.Contains; len = 1; break;
                case '>': op = QueryOp.Gt; len = 1; break;
                case '<': op = QueryOp.Lt; len = 1; break;
                default: return false;
            }
        }
        i += len; return true;
    }

    // ----- recursive-descent parser -----
    private sealed class Parser
    {
        private readonly List<Tok> _t; private int _p;
        public Parser(List<Tok> t) { _t = t; }
        private Tok Cur => _t[_p];
        private bool IsKeyword(string kw) => Cur.Type == TokType.Word && Cur.Text.Equals(kw, StringComparison.OrdinalIgnoreCase);

        public void ExpectEnd() { if (Cur.Type != TokType.End) throw new QueryParseException($"unexpected '{Cur.Text}'"); }

        public QueryNode ParseOr()
        {
            var left = ParseAnd();
            while (IsKeyword("OR")) { _p++; left = new OrNode(left, ParseAnd()); }
            return left;
        }

        private QueryNode ParseAnd()
        {
            var left = ParseNot();
            // Implicit AND between adjacent predicates is also accepted (e.g. "a==1 b==2").
            while (IsKeyword("AND") || StartsPrimary())
            {
                if (IsKeyword("AND")) _p++;
                left = new AndNode(left, ParseNot());
            }
            return left;
        }

        private QueryNode ParseNot()
        {
            if (IsKeyword("NOT")) { _p++; return new NotNode(ParseNot()); }
            return ParsePrimary();
        }

        private bool StartsPrimary()
            => Cur.Type == TokType.LParen ||
               (Cur.Type == TokType.Word && !IsKeyword("AND") && !IsKeyword("OR") && !IsKeyword("NOT")) ||
               Cur.Type == TokType.String;

        private QueryNode ParsePrimary()
        {
            if (Cur.Type == TokType.LParen)
            {
                _p++; var inner = ParseOr();
                if (Cur.Type != TokType.RParen) throw new QueryParseException("missing ')'");
                _p++; return inner;
            }
            if (Cur.Type == TokType.Word || Cur.Type == TokType.String)
            {
                var first = Cur; _p++;
                // field OP value ?
                if (first.Type == TokType.Word && Cur.Type == TokType.Op)
                {
                    var canonical = Canonical(first.Text)
                        ?? throw new QueryParseException($"unknown field '{first.Text}'");
                    var op = Cur.Op; _p++;
                    if (Cur.Type != TokType.Word && Cur.Type != TokType.String)
                        throw new QueryParseException($"expected a value after '{first.Text}'");
                    var val = Cur.Text; _p++;
                    if (op == QueryOp.Regex)
                    {
                        var rxErr = RegexError(val);
                        if (rxErr != null) throw new QueryParseException($"bad regex: {rxErr}");
                    }
                    // time ~ "12:34:56" [±2s]  →  the second (or the ± window) around that instant.
                    if (KindOf(canonical) == FieldKind.Time && (op == QueryOp.Contains || op == QueryOp.NotContains))
                    {
                        var center = TryParseTime(val) ?? throw new QueryParseException($"'{val}' is not a time");
                        QueryNode range;
                        if (Cur.Type == TokType.Word && TryParseWindow(Cur.Text, out var window))
                        {
                            _p++;
                            range = AroundTime(center, window);
                        }
                        else if (Cur.Type == TokType.Word && Cur.Text.StartsWith("±"))
                            throw new QueryParseException($"bad window '{Cur.Text}' (try ±2s, ±500ms, ±1m)");
                        else if (val.IndexOf('.') >= 0)
                            range = AroundTime(center, TimeSpan.FromMilliseconds(0.5)); // named to the millisecond
                        else
                        {
                            // No window: the whole second the value names.
                            var sec = new DateTime(center.Year, center.Month, center.Day, center.Hour, center.Minute, center.Second, center.Kind);
                            range = new AndNode(new PredicateNode("time", QueryOp.Ge, sec.ToString("o")),
                                                new PredicateNode("time", QueryOp.Lt, sec.AddSeconds(1).ToString("o")));
                        }
                        return op == QueryOp.Contains ? range : new NotNode(range);
                    }
                    return new PredicateNode(canonical, op, val);
                }
                // bare term → substring across all columns
                return new AnyContainsNode(first.Text);
            }
            throw new QueryParseException($"unexpected '{Cur.Text}'");
        }
    }

    private sealed class QueryParseException : Exception
    {
        public QueryParseException(string m) : base(m) { }
    }
}
