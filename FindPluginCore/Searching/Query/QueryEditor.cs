using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FindPluginCore.Searching.Query;

/// <summary>
/// Edits a query as a conjunction of clauses, the way the viewer's pivots compose: Follow, Filter in /
/// out and Around each ADD a clause to whatever is already in the search box, and a new clause on the
/// same axis REPLACES the old one (following process B after process A narrows to B, not to nothing).
/// Works on the AST and writes the text back, so the box always shows a query the user could have typed.
/// </summary>
public static class QueryEditor
{
    /// <summary>Query text for a node, in the language's own syntax (values always quoted).</summary>
    public static string Serialize(QueryNode node)
    {
        var sb = new StringBuilder();
        Write(node, sb, top: true);
        return sb.ToString();
    }

    private static void Write(QueryNode node, StringBuilder sb, bool top)
    {
        switch (node)
        {
            case AndNode a:
                if (!top) sb.Append('(');
                Write(a.L, sb, false); sb.Append(" AND "); Write(a.R, sb, false);
                if (!top) sb.Append(')');
                break;
            case OrNode o:
                sb.Append('(');
                Write(o.L, sb, false); sb.Append(" OR "); Write(o.R, sb, false);
                sb.Append(')');
                break;
            case NotNode n:
                sb.Append("NOT ");
                if (n.Inner is PredicateNode || n.Inner is AnyContainsNode) Write(n.Inner, sb, false);
                else { sb.Append('('); Write(n.Inner, sb, true); sb.Append(')'); }
                break;
            case AnyContainsNode any:
                sb.Append(Quote(any.Value));
                break;
            case TimeAroundNode ta:
                sb.Append("time ~ ").Append(Quote(ta.Center.ToString(ta.Center.Millisecond != 0 || ta.Window.HasValue ? "yyyy-MM-dd HH:mm:ss.fff" : "yyyy-MM-dd HH:mm:ss")));
                if (ta.Window.HasValue) sb.Append(" ±").Append(LogQuery.WindowText(ta.Window.Value));
                break;
            case PredicateNode p:
                sb.Append(p.Field).Append(' ').Append(OpText(p.Op)).Append(' ').Append(Quote(p.Value));
                break;
            default:
                throw new NotSupportedException(node?.GetType().Name ?? "null");
        }
    }

    public static string OpText(QueryOp op) => op switch
    {
        QueryOp.Eq => "==", QueryOp.Ne => "!=", QueryOp.Contains => "~", QueryOp.NotContains => "!~",
        QueryOp.Gt => ">", QueryOp.Lt => "<", QueryOp.Ge => ">=", QueryOp.Le => "<=", QueryOp.Regex => "=~",
        _ => "==",
    };

    /// <summary>Quote a value for the query language: only the quote and the backslash need escaping.</summary>
    public static string Quote(string v)
        => "\"" + (v ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    /// <summary>The top-level AND clauses of a query text. Plain text is one bare-term clause; text that
    /// does not parse is dropped (the caller is about to write a valid query).</summary>
    public static List<QueryNode> Clauses(string text)
    {
        var list = new List<QueryNode>();
        if (string.IsNullOrWhiteSpace(text)) return list;
        if (!LogQuery.LooksStructured(text)) { list.Add(new AnyContainsNode(text.Trim())); return list; }
        if (!LogQuery.TryParse(text, out var node, out _)) return list;
        Flatten(node, list);
        return list;
    }

    private static void Flatten(QueryNode node, List<QueryNode> into)
    {
        if (node is AndNode a) { Flatten(a.L, into); Flatten(a.R, into); }
        else into.Add(node);
    }

    /// <summary>
    /// Add <paramref name="clause"/> to <paramref name="existingText"/>: clauses for which
    /// <paramref name="replaces"/> is true are removed first (the same axis, being re-pivoted), an
    /// identical clause is not added twice, and the result is written back as text.
    /// </summary>
    public static string AddClause(string existingText, QueryNode clause, Func<QueryNode, bool> replaces)
    {
        var clauses = Clauses(existingText);
        var added = Serialize(clause);
        bool alreadyThere = clauses.Any(c => Serialize(c) == added);
        clauses.RemoveAll(c => Serialize(c) != added && replaces(c));
        if (!alreadyThere) clauses.Add(clause); // an identical clause keeps its place rather than moving to the end
        return string.Join(" AND ", clauses.Select(Serialize));
    }

    /// <summary>Every predicate leaf under a clause (through AND / OR / NOT).</summary>
    public static IEnumerable<PredicateNode> Leaves(QueryNode node)
    {
        switch (node)
        {
            case PredicateNode p: yield return p; break;
            case AndNode a: foreach (var l in Leaves(a.L)) yield return l; foreach (var r in Leaves(a.R)) yield return r; break;
            case OrNode o: foreach (var l in Leaves(o.L)) yield return l; foreach (var r in Leaves(o.R)) yield return r; break;
            case NotNode n: foreach (var l in Leaves(n.Inner)) yield return l; break;
        }
    }

    /// <summary>True when the clause pins one of <paramref name="fields"/> to a value (an <c>==</c> leaf on
    /// it, anywhere in the clause). This is what a new Follow / Filter-in on that field supersedes.</summary>
    public static bool PinsAnyOf(QueryNode clause, IEnumerable<string> fields)
    {
        var set = new HashSet<string>(fields.Select(f => LogQuery.Canonical(f) ?? f), StringComparer.OrdinalIgnoreCase);
        return Leaves(clause).Any(p => p.Op == QueryOp.Eq && set.Contains(p.Field));
    }

    /// <summary>True when the clause is a time range (<c>time ~ … ±w</c>, or ordered comparisons on <c>time</c>
    /// and nothing else) - what a new Around supersedes.</summary>
    public static bool IsTimeRange(QueryNode clause)
    {
        if (clause is TimeAroundNode) return true;
        if (clause is NotNode n && n.Inner is TimeAroundNode) return true;
        var leaves = Leaves(clause).ToList();
        return leaves.Count > 0
            && leaves.Any(p => p.Field == "time" && p.Op is QueryOp.Ge or QueryOp.Le or QueryOp.Gt or QueryOp.Lt)
            && leaves.All(p => p.Field == "time");
    }
}
