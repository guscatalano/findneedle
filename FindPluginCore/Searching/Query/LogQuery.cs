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
public enum QueryOp { Eq, Ne, Contains, NotContains, Gt, Lt, Ge, Le }

/// <summary>How a field's value is stored / compared (Level is an int enum in SQL; Time is an ISO string).</summary>
public enum FieldKind { Text, Level, Time }

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
        var actual = g(Field) ?? "";
        switch (Op)
        {
            case QueryOp.Eq:          return string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase);
            case QueryOp.Ne:          return !string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase);
            case QueryOp.Contains:    return actual.IndexOf(Value, StringComparison.OrdinalIgnoreCase) >= 0;
            case QueryOp.NotContains: return actual.IndexOf(Value, StringComparison.OrdinalIgnoreCase) < 0;
            default:                  return CompareOrdered(actual);
        }
    }

    private bool CompareOrdered(string actual)
    {
        int cmp;
        var av = LogQuery.TryParseTime(actual);
        var bv = LogQuery.TryParseTime(Value);
        if (LogQuery.KindOf(Field) == FieldKind.Time && av.HasValue && bv.HasValue)
            cmp = DateTime.Compare(av.Value, bv.Value);
        else if (double.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var an)
                 && double.TryParse(Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var bn))
            cmp = an.CompareTo(bn);
        else
            cmp = string.Compare(actual, Value, StringComparison.OrdinalIgnoreCase);

        return Op switch
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
            ["eventid"] = ("EventId", FieldKind.Text),
            ["channel"] = ("Channel", FieldKind.Text),
            ["machinename"] = ("MachineName", FieldKind.Text),
            ["username"] = ("Username", FieldKind.Text),
            ["opcode"] = ("OpCode", FieldKind.Text),
            ["time"] = ("LogTime", FieldKind.Time),
        };

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
            ["eventid"] = "eventid", ["event"] = "eventid", ["id"] = "eventid",
            ["channel"] = "channel",
            ["machine"] = "machinename", ["machinename"] = "machinename", ["computer"] = "machinename",
            ["user"] = "username", ["username"] = "username",
            ["opcode"] = "opcode",
            ["time"] = "time", ["timestamp"] = "time",
        };

    /// <summary>The canonical fields (for the UI help text).</summary>
    public static IEnumerable<string> Fields => Map.Keys;

    public static bool IsField(string name) => name != null && Alias.ContainsKey(name);
    public static string Canonical(string name) => Alias.TryGetValue(name ?? "", out var c) ? c : null;
    public static (string col, FieldKind kind) ColumnOf(string canonical) => Map[canonical];
    public static FieldKind KindOf(string canonical) => Map.TryGetValue(canonical ?? "", out var v) ? v.kind : FieldKind.Text;

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
        => DateTime.TryParse(s, CultureInfo.InvariantCulture,
               DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault, out var dt)
           ? dt : (DateTime?)null;

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
                while (i < n && s[i] != q) { if (s[i] == '\\' && i + 1 < n) i++; sb.Append(s[i++]); }
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
