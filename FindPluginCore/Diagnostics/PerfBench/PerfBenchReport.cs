#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace FindPluginCore.Diagnostics.PerfBench;

/// <summary>
/// Renders a <see cref="PerfBenchResult"/> to its two outputs: the JSON submission and a self-contained
/// HTML report (inline CSS, no external/CDN dependencies) written for a general audience — a plain-English
/// summary up top, numbers grouped by the question a person actually asks ("how long to open a log?",
/// "how fast is search?", "does it stay smooth?"), and the raw table kept for people who want it.
/// Both outputs come from the one result object, so the published report can't drift from the numbers.
/// </summary>
public static class PerfBenchReport
{
    public static void WriteJson(PerfBenchResult r, string path) => File.WriteAllText(path, r.ToJson());
    public static void WriteHtml(PerfBenchResult r, string path) => File.WriteAllText(path, RenderHtml(r));

    public static string RenderHtml(PerfBenchResult r)
    {
        var big = r.Scenarios
            .Where(s => s.Kind == "engine" && s.Cold != null && s.Cold.ContainsKey("ingestMs"))
            .OrderByDescending(s => s.Rows).FirstOrDefault();

        var sb = new StringBuilder(9000);
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>FindNeedle Performance Benchmark</title><style>").Append(Css).Append("</style></head><body><main>");

        sb.Append("<header><div class=\"eyebrow\">FindNeedle</div><h1>Performance report</h1>")
          .Append("<p class=\"meta\">how fast this computer runs FindNeedle")
          .Append("<span class=\"dot\"></span>").Append(E(ShortDate(r.TimestampUtc)))
          .Append("<span class=\"dot\"></span>median of ").Append(r.Repeats).Append(" runs</p></header>");

        // Plain-English summary — the part a non-technical person reads.
        if (big?.Cold is { } bc)
        {
            double ready = bc.GetValueOrDefault("ingestMs") + bc.GetValueOrDefault("indexBuildMs");
            sb.Append("<p class=\"summary\">On this computer, FindNeedle opened a <b>")
              .Append(big.Rows.ToString("N0", CultureInfo.InvariantCulture)).Append("-line</b> log — ready to read and search — in <b>")
              .Append(Word(ready)).Append("</b>. After that it finds any single line in about <b>")
              .Append(Word(bc.GetValueOrDefault("searchSelectiveMs"))).Append("</b>");
            if (bc.TryGetValue("firstPage500Ms", out var fp))
                sb.Append(" and paints the first screen of results in <b>").Append(Word(fp)).Append("</b>");
            sb.Append(". ");
            if (big.Ratios.TryGetValue("ftsVsScan", out var fx) && fx > 1)
                sb.Append("Its built-in index makes that search about <b>").Append(Num(fx))
                  .Append("×</b> faster than scanning every line the slow way. ");
            sb.Append("Bigger number of cores and faster disks make the times below shorter.</p>");
        }

        sb.Append("<p class=\"about\"><b>What this measured:</b> plain-text log lines generated on this "
          + "computer — reading them in, building the search index, searching, and scrolling. It did "
          + "<b>not</b> decode a real ETW / WPP trace or Windows event log — that decoding is separate, "
          + "heavier work that isn't part of this test yet. Storage engine: FindNeedle's on-disk "
          + "<b>SQLite</b> index with full-text search, which is what it uses for logs this size (smaller "
          + "logs, under ~50,000 lines, use a faster in-memory engine instead).</p>");

        AppendContentionWarning(sb, r);

        // Grouped by the question a user asks — plain labels, a one-line explanation each.
        AppendGroup(sb, "Opening a log", "How long before you can start reading and searching.", OpeningTiles(big));
        AppendGroup(sb, "Searching", "How quickly it finds things once the log is open.", SearchTiles(big));
        AppendGroup(sb, "Staying smooth", "How responsive scrolling and jumping around feel.", ResponsiveTiles(big));

        AppendScaling(sb, r);

        // This computer + this run
        sb.Append("<section class=\"twocol\">");
        InfoCard(sb, "This computer", new (string, string)[]
        {
            ("Processor", r.Machine.CpuModel),
            ("Cores", r.Machine.LogicalCores + " cores"),
            ("Memory", Num(r.Machine.RamGB) + " GB"),
            ("System", r.Machine.Os),
        });
        InfoCard(sb, "This run", new (string, string)[]
        {
            ("Other apps' CPU use", r.SystemLoad.PeakForeignCpuPercentDuring >= 15
                ? Num(r.SystemLoad.PeakForeignCpuPercentDuring) + "% — busy" : "quiet"),
            ("Free memory", Num(r.SystemLoad.AvailableRamGB) + " GB"),
            ("Can decode Windows WPP traces", r.SystemLoad.WdkPresent ? "yes" : "no (needs the WDK)"),
            ("FindNeedle build", string.IsNullOrEmpty(r.App.Version) ? "developer build" : r.App.Version),
        });
        sb.Append("</section>");

        // The precise numbers, for people who want them.
        sb.Append("<details class=\"raw\"><summary>Show the raw numbers (milliseconds)</summary>");
        sb.Append("<table><thead><tr>")
          .Append("<th class=\"l\">Test</th><th>Lines</th><th>Load</th><th>Index</th><th>Find one</th><th>Match all</th>")
          .Append("<th>First page</th><th>Jump to end</th></tr></thead><tbody>");
        foreach (var s in r.Scenarios)
        {
            var c = s.Cold ?? new Dictionary<string, double>();
            sb.Append("<tr><td class=\"l mono\">").Append(E(PlainTest(s.Id))).Append("</td><td>")
              .Append(s.Rows.ToString("N0", CultureInfo.InvariantCulture))
              .Append("</td><td>").Append(Ms(c, "ingestMs"))
              .Append("</td><td>").Append(Ms(c, "indexBuildMs"))
              .Append("</td><td>").Append(Ms(c, "searchSelectiveMs"))
              .Append("</td><td>").Append(Ms(c, "searchWorstMs"))
              .Append("</td><td>").Append(Ms(c, "firstPage5000Ms"))
              .Append("</td><td>").Append(Ms(c, "jumpToLastMs"))
              .Append("</td></tr>");
        }
        sb.Append("</tbody></table></details>");

        sb.Append("<footer>Times are for <b>this</b> computer under its load at run time; a busier or slower "
          + "machine will read higher. The speed-up ratios (like “×  faster than scanning”) are about the "
          + "software, so they hold up on any hardware.</footer>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    // ---- tile groups (plain labels) ----

    private readonly record struct Tile(string value, string unit, string label, string sub);

    private static List<Tile> OpeningTiles(PerfBenchScenario? big)
    {
        var t = new List<Tile>();
        if (big?.Cold is not { } c) return t;
        var sp = big.Spread; var sub = Rows(big.Rows);
        if (c.TryGetValue("ingestMs", out var i)) t.Add(TimeTile(i, "Read the log in", Range(sub, sp, "ingestMs")));
        if (c.TryGetValue("indexBuildMs", out var x)) t.Add(TimeTile(x, "Build the search index", Range(sub, sp, "indexBuildMs")));
        if (c.ContainsKey("ingestMs") && c.ContainsKey("indexBuildMs"))
            t.Add(TimeTile(c["ingestMs"] + c["indexBuildMs"], "Ready to use", sub));
        return t;
    }

    private static List<Tile> SearchTiles(PerfBenchScenario? big)
    {
        var t = new List<Tile>();
        if (big?.Cold is not { } c) return t;
        var sub = Rows(big.Rows);
        if (c.TryGetValue("searchSelectiveMs", out var sel)) t.Add(TimeTile(sel, "Find one specific line", sub));
        if (c.TryGetValue("searchWorstMs", out var w)) t.Add(TimeTile(w, "Filter to a common word", sub));
        if (big.Ratios.TryGetValue("ftsVsScan", out var fx) && fx > 0)
            t.Add(new Tile(Num(fx), "×", "Faster than no index", "the index earns its keep"));
        return t;
    }

    private static List<Tile> ResponsiveTiles(PerfBenchScenario? big)
    {
        var t = new List<Tile>();
        if (big?.Cold is not { } c) return t;
        var sub = Rows(big.Rows);
        if (c.TryGetValue("firstPage500Ms", out var a)) t.Add(TimeTile(a, "First page (500 rows)", sub));
        if (c.TryGetValue("firstPage5000Ms", out var b)) t.Add(TimeTile(b, "First page (5,000 rows)", sub));
        if (c.TryGetValue("jumpToLastMs", out var j)) t.Add(TimeTile(j, "Jump to the very end", sub));
        return t;
    }

    private static void AppendGroup(StringBuilder sb, string title, string desc, List<Tile> tiles)
    {
        if (tiles.Count == 0) return;
        sb.Append("<section><h2>").Append(E(title)).Append("</h2><p class=\"gdesc\">").Append(E(desc)).Append("</p><div class=\"tiles\">");
        foreach (var t in tiles)
            sb.Append("<div class=\"tile\"><div class=\"num\">").Append(E(t.value))
              .Append("<span class=\"unit\">").Append(E(t.unit)).Append("</span></div>")
              .Append("<div class=\"tlabel\">").Append(E(t.label)).Append("</div>")
              .Append("<div class=\"tsub\">").Append(E(t.sub)).Append("</div></div>");
        sb.Append("</div></section>");
    }

    /// <summary>All engine sizes side by side — the scaling story (open-time grows, search stays fast).</summary>
    private static void AppendScaling(StringBuilder sb, PerfBenchResult r)
    {
        var engs = r.Scenarios
            .Where(s => s.Kind == "engine" && s.Cold != null && s.Cold.ContainsKey("ingestMs"))
            .OrderBy(s => s.Rows).ToList();
        if (engs.Count < 2) return;

        sb.Append("<section><h2>How it scales with log size</h2>")
          .Append("<p class=\"gdesc\">Bigger logs take longer to open, but finding things and scrolling stay fast.</p>")
          .Append("<table class=\"scale\"><thead><tr><th class=\"l\">Log size</th><th class=\"l\">Storage</th><th>Ready to use</th>")
          .Append("<th>Find a line</th><th>First page</th><th>Faster than<br>no index</th></tr></thead><tbody>");
        foreach (var s in engs)
        {
            var c = s.Cold!;
            double ready = c.GetValueOrDefault("ingestMs") + c.GetValueOrDefault("indexBuildMs");
            sb.Append("<tr><td class=\"l\"><b>").Append(E(Rows(s.Rows))).Append("</b></td>")
              .Append("<td class=\"l muted\">").Append(E(TierLabel(s.StorageTierChosen))).Append("</td><td>")
              .Append(FmtTimeStr(ready)).Append("</td><td>").Append(TimeCell(c, "searchSelectiveMs"))
              .Append("</td><td>").Append(TimeCell(c, "firstPage5000Ms"))
              .Append("</td><td>").Append(s.Ratios.TryGetValue("ftsVsScan", out var f) ? Num(f) + "×" : "—")
              .Append("</td></tr>");
        }
        sb.Append("</tbody></table></section>");
    }

    private static string TierLabel(string? t) => t switch
    {
        "Sqlite" => "SQLite",
        "InMemory" => "In-memory",
        "Hybrid" => "Hybrid",
        _ => string.IsNullOrEmpty(t) ? "—" : t!,
    };

    private static string FmtTimeStr(double ms) { var (v, u) = FmtTimeParts(ms); return v + " " + u; }
    private static string TimeCell(Dictionary<string, double> c, string key)
        => c.TryGetValue(key, out var v) ? FmtTimeStr(v) : "<span class=\"muted\">—</span>";

    private static void AppendContentionWarning(StringBuilder sb, PerfBenchResult r)
    {
        bool wide = r.Scenarios.Any(s => s.Spread != null && s.Spread.Values.Any(m => m.Min > 0 && m.Max / m.Min > 1.3));
        if (r.SystemLoad.PeakForeignCpuPercentDuring < 15 && !wide) return;
        var bits = new List<string>();
        if (r.SystemLoad.PeakForeignCpuPercentDuring >= 15)
            bits.Add($"other apps were using up to <b>{Num(r.SystemLoad.PeakForeignCpuPercentDuring)}%</b> of the processor");
        if (wide) bits.Add("the repeat runs disagreed a fair bit");
        sb.Append("<p class=\"warn\">⚠ Heads up — ").Append(string.Join(", and ", bits))
          .Append(", so the times below are a bit slower than this computer's best. Close other apps and re-run for cleaner numbers.</p>");
    }

    // ---- formatting ----

    private static Tile TimeTile(double ms, string label, string sub)
    {
        var (v, u) = FmtTimeParts(ms);
        return new Tile(v, u, label, sub);
    }

    private static string Range(string sub, Dictionary<string, PerfBenchMinMax>? sp, string key)
        => sp != null && sp.TryGetValue(key, out var mm) && mm.Max > mm.Min ? sub + " · " + FmtRange(mm.Min, mm.Max) : sub;

    private static (string v, string u) FmtTimeParts(double ms)
        => ms >= 1000 ? ((ms / 1000.0).ToString("0.#", CultureInfo.InvariantCulture), "s") : (Num(ms), "ms");

    private static string FmtRange(double min, double max)
    {
        var (v1, u1) = FmtTimeParts(min);
        var (v2, u2) = FmtTimeParts(max);
        return u1 == u2 ? $"{v1}–{v2} {u1}" : $"{v1} {u1} – {v2} {u2}";
    }

    /// <summary>Spelled-out duration for the plain-English summary.</summary>
    private static string Word(double ms)
    {
        if (ms < 1) return "under a millisecond";
        if (ms < 1000) return $"{Num(ms)} millisecond" + (Math.Round(ms) == 1 ? "" : "s");
        double s = ms / 1000.0;
        return $"{s.ToString("0.#", CultureInfo.InvariantCulture)} second" + (Math.Abs(s - 1) < 0.05 ? "" : "s");
    }

    private static string Rows(long n) => RowsShort(n) + " lines";
    private static string RowsShort(long n) => n >= 1_000_000 ? $"{n / 1_000_000}M" : n >= 1000 ? $"{n / 1000}k" : n.ToString();

    private static string PlainTest(string id) => id.StartsWith("engine.text.") ? id.Substring("engine.text.".Length) + "-line log" : id;

    private static void InfoCard(StringBuilder sb, string title, (string k, string v)[] rows)
    {
        sb.Append("<div class=\"card\"><h3>").Append(E(title)).Append("</h3><dl>");
        foreach (var (k, v) in rows)
            sb.Append("<dt>").Append(E(k)).Append("</dt><dd>").Append(E(string.IsNullOrEmpty(v) ? "—" : v)).Append("</dd>");
        sb.Append("</dl></div>");
    }

    private static string Ms(Dictionary<string, double> c, string key)
        => c.TryGetValue(key, out var v) ? "<span class=\"mono\">" + Num(v) + "</span>" : "<span class=\"muted\">—</span>";

    private static string ShortDate(string iso)
        => DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var d)
            ? d.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture) : iso;

    private static string Num(double v)
        => Math.Abs(v - Math.Round(v)) < 0.05 ? Math.Round(v).ToString("N0", CultureInfo.InvariantCulture)
                                              : v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private const string Css = @"
:root{
  --bg:#f4f2ee; --card:#fffdfa; --border:#e7e3da; --ink:#211e19; --muted:#77726a; --faint:#a49e93;
  --accent:#5b4bd6; --warn:#c67611; --warn-weak:#fdf1df; color-scheme:light dark;
}
@media(prefers-color-scheme:dark){:root{
  --bg:#141219; --card:#1c1a24; --border:#2c2836; --ink:#ece8f4; --muted:#9c96ab; --faint:#6a6478;
  --accent:#a394ff; --warn:#e0a250; --warn-weak:#2a2012;
}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);
  font:16px/1.6 ui-sans-serif,-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;-webkit-font-smoothing:antialiased}
main{max-width:820px;margin:0 auto;padding:3rem 1.5rem 4rem}
.mono{font-family:ui-monospace,'Cascadia Code',Consolas,monospace;font-variant-numeric:tabular-nums}
header{margin-bottom:1.6rem}
.eyebrow{color:var(--accent);font-weight:700;letter-spacing:.14em;text-transform:uppercase;font-size:.72rem}
h1{font-size:2rem;line-height:1.1;margin:.35rem 0 .4rem;letter-spacing:-.02em}
.meta{color:var(--muted);font-size:.85rem;margin:0;display:flex;align-items:center;flex-wrap:wrap;gap:.55rem}
.dot{width:3px;height:3px;border-radius:50%;background:var(--faint);display:inline-block}
.summary{font-size:1.12rem;line-height:1.65;margin:1.4rem 0 .5rem}
.summary b{color:var(--accent);font-weight:700}
.about{color:var(--muted);font-size:.9rem;line-height:1.6;margin:.5rem 0;padding:.75rem 1rem;
  border-left:3px solid var(--accent);background:var(--card);border-radius:0 10px 10px 0}
.about b{color:var(--ink);font-weight:600}
section{margin:2rem 0}
h2{font-size:1.15rem;margin:0 0 .15rem;letter-spacing:-.01em}
.gdesc{color:var(--muted);font-size:.9rem;margin:0 0 .9rem}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:.9rem}
.tile{background:var(--card);border:1px solid var(--border);border-radius:14px;padding:1.05rem 1.2rem}
.num{font-size:2rem;font-weight:700;line-height:1;letter-spacing:-.02em;color:var(--accent);font-variant-numeric:tabular-nums}
.unit{font-size:.95rem;font-weight:600;margin-left:.15rem;color:var(--accent);opacity:.7}
.tlabel{margin-top:.55rem;font-weight:600;font-size:.95rem}
.tsub{color:var(--muted);font-size:.8rem;margin-top:.1rem}
.twocol{display:grid;grid-template-columns:1fr 1fr;gap:.9rem}
@media(max-width:640px){.twocol{grid-template-columns:1fr}}
.card{background:var(--card);border:1px solid var(--border);border-radius:14px;padding:1.05rem 1.25rem}
.card h3{margin:0 0 .7rem;font-size:.78rem;letter-spacing:.06em;text-transform:uppercase;color:var(--muted)}
dl{display:grid;grid-template-columns:auto 1fr;gap:.45rem 1rem;margin:0}
dt{color:var(--muted)}dd{margin:0;text-align:right;font-variant-numeric:tabular-nums}
.warn{background:var(--warn-weak);border:1px solid var(--warn);border-radius:12px;padding:.75rem 1rem;font-size:.9rem;margin:1.4rem 0}
.warn b{color:var(--warn)}
.raw{margin:2rem 0 0;font-size:.9rem}
.raw summary{cursor:pointer;color:var(--muted);font-size:.85rem;margin-bottom:.6rem}
table{width:100%;border-collapse:collapse;font-variant-numeric:tabular-nums;font-size:.85rem}
th,td{padding:.4rem .55rem;text-align:right;border-bottom:1px solid var(--border)}
th{font-size:.68rem;letter-spacing:.03em;text-transform:uppercase;color:var(--muted);font-weight:600}
th.l,td.l{text-align:left}tbody tr:last-child td{border-bottom:0}
table.scale{font-size:.98rem}
table.scale th{font-size:.7rem}
table.scale th,table.scale td{padding:.6rem .75rem}
.raw table tbody tr:last-child td{border-bottom:0}
.muted{color:var(--faint)}
footer{margin-top:2.5rem;padding-top:1rem;border-top:1px solid var(--border);color:var(--muted);font-size:.82rem;line-height:1.55}
";
}
