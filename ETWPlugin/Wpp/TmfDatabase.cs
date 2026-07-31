using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace findneedle.Wpp;

/// <summary>
/// A single WPP trace statement's format record, parsed from a `.tmf` <c>#typev</c> block. This is the
/// managed equivalent of what tracefmt loads: the printf-style format string + the ordered, typed argument
/// list needed to render one event. Keyed on (message GUID, message number).
/// </summary>
public sealed class TmfEntry
{
    /// <summary>The source-file / message GUID this statement belongs to (the .tmf's own GUID) — matches the
    /// classic WPP event's trace GUID on the wire.</summary>
    public Guid MessageGuid { get; init; }

    /// <summary>The message number within the GUID (the <c>#typev &lt;tag&gt; &lt;N&gt;</c> N) — matches the
    /// event's message number on the wire.</summary>
    public int MessageNumber { get; init; }

    /// <summary>The printf-style format template, e.g. <c>%0Foo id=%10!d! status=0x%11!x!</c>. <c>%0</c> is the
    /// WPP prefix; user args are <c>%10</c>+.</summary>
    public string Format { get; init; } = "";

    /// <summary>The ordered argument descriptors (by arg number), read from the <c>{ ... }</c> block.</summary>
    public IReadOnlyList<TmfArg> Args { get; init; } = Array.Empty<TmfArg>();

    /// <summary>The tag from the #typev line, e.g. <c>WppEmitter_cpp36</c> (source-file + line).</summary>
    public string Tag { get; init; } = "";

    /// <summary>WPP LEVEL/FLAGS name from the trailing comment (e.g. TRACE_GENERAL), if present.</summary>
    public string Level { get; init; } = "";

    /// <summary>FUNC name from the trailing comment, if present.</summary>
    public string Func { get; init; } = "";

    /// <summary>Component name from the GUID header line (e.g. <c>findneedle</c>).</summary>
    public string Component { get; init; } = "";
}

/// <summary>One argument slot in a <see cref="TmfEntry"/>: its position (the <c>-- N</c>) and WPP type name.</summary>
public readonly record struct TmfArg(int ArgNumber, string TypeName);

/// <summary>
/// Parses WPP `.tmf` files into a lookup of <see cref="TmfEntry"/> by (message GUID, message number) — the
/// managed stand-in for tracefmt's internal TMF table. tracefmt (and traceview) are thin front-ends over the
/// OS WPP format engine; this is the first piece of doing that formatting in-process, with no WDK dependency.
///
/// TMF grammar (from `tracepdb -f`):
/// <code>
/// // comments
/// &lt;guid&gt; &lt;component&gt; // SRC=&lt;file&gt; MJ= MN=
/// #typev &lt;tag&gt; &lt;msgNum&gt; "&lt;format&gt;" // LEVEL=&lt;lvl&gt; FUNC=&lt;func&gt;
/// {
/// &lt;expr&gt;, &lt;TypeName&gt; -- &lt;argNum&gt;
/// ...
/// }
/// </code>
/// A file can hold multiple GUID blocks (one per source file); each #typev under the current GUID becomes an entry.
/// </summary>
public sealed class TmfDatabase
{
    private readonly Dictionary<(Guid, int), TmfEntry> _entries = new();

    public int Count => _entries.Count;

    public bool TryGet(Guid messageGuid, int messageNumber, out TmfEntry entry)
        => _entries.TryGetValue((messageGuid, messageNumber), out entry);

    public IEnumerable<TmfEntry> Entries => _entries.Values;

    /// <summary>Load and merge every <c>*.tmf</c> under <paramref name="dir"/> (recursively). Missing dir → empty.</summary>
    public static TmfDatabase LoadDirectory(string dir)
    {
        var db = new TmfDatabase();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return db;
        foreach (var f in Directory.EnumerateFiles(dir, "*.tmf", SearchOption.AllDirectories))
        {
            try { db.AddFile(f); } catch { /* skip a malformed TMF; others still load */ }
        }
        return db;
    }

    public void AddFile(string path) => AddText(File.ReadAllText(path));

    // Header:  <guid> <component> // SRC=<file> MJ= MN=
    private static readonly Regex GuidHeader = new(
        @"^([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+(\S+)",
        RegexOptions.Compiled);

    // #typev <tag> <msgNum> "<format>" // LEVEL=<lvl> FUNC=<func>
    private static readonly Regex TypevLine = new(
        "^#typev\\s+(\\S+)\\s+(\\d+)\\s+\"(.*)\"(.*)$", RegexOptions.Compiled);

    // Arg line inside { }:  <expr>, <TypeName> -- <argNum>
    private static readonly Regex ArgLine = new(
        @"^.*?,\s*(\w+)\s*--\s*(\d+)\s*$", RegexOptions.Compiled);

    /// <summary>Parse one TMF's text and merge its entries. Public so the parser is unit-testable directly.</summary>
    public void AddText(string text)
    {
        Guid currentGuid = Guid.Empty;
        string currentComponent = "";
        using var reader = new StringReader(text);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("//")) continue;

            var gh = GuidHeader.Match(trimmed);
            if (gh.Success && Guid.TryParse(gh.Groups[1].Value, out var g))
            {
                currentGuid = g;
                currentComponent = gh.Groups[2].Value;
                continue;
            }

            var tv = TypevLine.Match(trimmed);
            if (tv.Success)
            {
                var tag = tv.Groups[1].Value;
                var msgNum = int.Parse(tv.Groups[2].Value, CultureInfo.InvariantCulture);
                var format = tv.Groups[3].Value;
                var trailing = tv.Groups[4].Value;

                // The arg block ( { ... } ) follows on subsequent lines up to the closing brace.
                var args = new List<TmfArg>();
                string bodyLine;
                while ((bodyLine = reader.ReadLine()) != null)
                {
                    var b = bodyLine.Trim();
                    if (b == "{" ) continue;
                    if (b == "}" || b.StartsWith("}")) break;
                    if (b.Length == 0) continue;
                    var am = ArgLine.Match(b);
                    if (am.Success)
                        args.Add(new TmfArg(int.Parse(am.Groups[2].Value, CultureInfo.InvariantCulture),
                                            am.Groups[1].Value));
                }
                args.Sort((x, y) => x.ArgNumber.CompareTo(y.ArgNumber));

                var entry = new TmfEntry
                {
                    MessageGuid = currentGuid,
                    MessageNumber = msgNum,
                    Format = format,
                    Args = args,
                    Tag = tag,
                    Component = currentComponent,
                    Level = ExtractTag(trailing, "LEVEL"),
                    Func = ExtractTag(trailing, "FUNC"),
                };
                _entries[(currentGuid, msgNum)] = entry; // last definition wins
            }
        }
    }

    private static string ExtractTag(string trailingComment, string key)
    {
        var m = Regex.Match(trailingComment ?? "", key + @"=(\S+)");
        return m.Success ? m.Groups[1].Value : "";
    }
}
