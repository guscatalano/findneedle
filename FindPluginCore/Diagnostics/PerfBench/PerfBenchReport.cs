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
/// HTML report (inline CSS, no external/CDN dependencies). Both come from the one result object, so the
/// author's published report can never drift from the numbers.
///
/// Form choices (why it looks the way it does): the headline ratios are incommensurable (× vs µs/row),
/// so they are <b>stat tiles</b> — hero numbers, never bars on a shared scale. Milliseconds live in one
/// clean table, labeled "on this machine." One restrained accent; warm-neutral surfaces; validated
/// light + dark.
/// </summary>
public static class PerfBenchReport
{
    public static void WriteJson(PerfBenchResult r, string path) => File.WriteAllText(path, r.ToJson());

    public static void WriteHtml(PerfBenchResult r, string path) => File.WriteAllText(path, RenderHtml(r));

    public static string RenderHtml(PerfBenchResult r)
    {
        var sb = new StringBuilder(8192);
        sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("<title>FindNeedle Performance Benchmark</title>");
        sb.Append("<style>").Append(Css).Append("</style></head><body><main>");

        // Header
        sb.Append("<header><div class=\"eyebrow\">FindNeedle</div>")
          .Append("<h1>Performance Benchmark</h1>")
          .Append("<p class=\"meta\">")
          .Append("v").Append(r.BenchmarkVersion)
          .Append("<span class=\"dot\"></span>preset <b>").Append(E(r.Preset)).Append("</b>")
          .Append("<span class=\"dot\"></span>median of ").Append(r.Repeats)
          .Append("<span class=\"dot\"></span>").Append(E(ShortDate(r.TimestampUtc)))
          .Append("</p></header>");

        // Headline ratios as stat tiles (cross-machine — the comparable numbers)
        AppendTiles(sb, "Headline", "compares across machines", HeadlineTiles(r));
        // Wall-clock times as stat tiles (per-machine — what a run actually costs)
        AppendTiles(sb, "Time", "on this machine", TimeTiles(r));
        // Paging / viewer-responsiveness latency (per-machine)
        AppendTiles(sb, "Paging", "first page & jump — on this machine", PagingTiles(r));

        // Make a noisy run look noisy, rather than printing a confident median it can't back up.
        bool wideSpread = r.Scenarios.Any(s => s.Spread != null && s.Spread.Values.Any(m => m.Min > 0 && m.Max / m.Min > 1.3));
        if (r.SystemLoad.PeakForeignCpuPercentDuring >= 15 || wideSpread)
        {
            var bits = new List<string>();
            if (r.SystemLoad.PeakForeignCpuPercentDuring >= 15)
                bits.Add($"other apps used up to <b>{Num(r.SystemLoad.PeakForeignCpuPercentDuring)}%</b> CPU during it");
            if (wideSpread) bits.Add("some repeats disagreed (see the ranges under the times)");
            sb.Append("<p class=\"warn\">⚠ This run looks contended: ").Append(string.Join(", and ", bits))
              .Append(". Absolute milliseconds are inflated here — the ratios hold up better; re-run with fewer apps open for cleaner times.</p>");
        }

        // Machine + run context
        sb.Append("<section class=\"twocol\">");
        InfoCard(sb, "Machine", new (string, string)[]
        {
            ("CPU", r.Machine.CpuModel),
            ("Cores", r.Machine.LogicalCores.ToString()),
            ("Memory", Num(r.Machine.RamGB) + " GB"),
            ("OS", r.Machine.Os),
            ("Arch", r.App.Arch),
        });
        InfoCard(sb, "Run context", new (string, string)[]
        {
            ("App", string.IsNullOrEmpty(r.App.Version) ? "dev build" : r.App.Version + " · " + r.App.Configuration),
            ("Runtime", r.App.Runtime),
            ("Idle CPU before run", Num(r.SystemLoad.IdleCpuPercentBefore) + " %"),
            ("Other-app CPU · peak", Num(r.SystemLoad.PeakForeignCpuPercentDuring) + " %"),
            ("Free memory", Num(r.SystemLoad.AvailableRamGB) + " GB"),
            ("WPP decode (WDK)", r.SystemLoad.WdkPresent ? "available" : "not installed"),
        });
        sb.Append("</section>");

        // Timings table (on this machine)
        sb.Append("<section><h2>Timings<span class=\"tag\">on this machine · ms</span></h2>");
        sb.Append("<table><thead><tr>")
          .Append("<th class=\"l\">Scenario</th><th>Rows</th><th>Ingest</th><th>Index</th>")
          .Append("<th>Search · selective</th><th>Search · worst</th><th class=\"l\"></th></tr></thead><tbody>");
        foreach (var s in r.Scenarios)
        {
            var c = s.Cold ?? new Dictionary<string, double>();
            sb.Append("<tr><td class=\"l mono\">").Append(E(s.Id)).Append("</td><td>")
              .Append(s.Rows.ToString("N0", CultureInfo.InvariantCulture))
              .Append("</td><td>").Append(Ms(c, "ingestMs"))
              .Append("</td><td>").Append(Ms(c, "indexBuildMs"))
              .Append("</td><td>").Append(Ms(c, "searchSelectiveMs"))
              .Append("</td><td>").Append(Ms(c, "searchWorstMs"))
              .Append("</td><td class=\"l\">")
              .Append(s.Status == "ok" ? "<span class=\"ok\">ok</span>" : "<span class=\"skip\">" + E(s.SkipReason ?? "skipped") + "</span>")
              .Append("</td></tr>");
        }
        sb.Append("</tbody></table></section>");

        if (r.Notes.Count > 0)
        {
            sb.Append("<section><h2>Notes</h2><ul class=\"notes\">");
            foreach (var n in r.Notes) sb.Append("<li>").Append(E(n)).Append("</li>");
            sb.Append("</ul></section>");
        }

        sb.Append("<footer>Ratios compare across any hardware; milliseconds are specific to this machine and its load at run time")
          .Append("<span class=\"dot\"></span>idle CPU was ").Append(Num(r.SystemLoad.IdleCpuPercentBefore)).Append("%")
          .Append("<span class=\"dot\"></span>run ").Append(E(r.RunId)).Append("</footer>");
        sb.Append("</main></body></html>");
        return sb.ToString();
    }

    // ---- tiles ----

    private readonly record struct Tile(string value, string unit, string label, string sub);

    private static void AppendTiles(StringBuilder sb, string title, string tag, List<Tile> tiles)
    {
        if (tiles.Count == 0) return;
        sb.Append("<section><h2>").Append(E(title)).Append("<span class=\"tag\">").Append(E(tag))
          .Append("</span></h2><div class=\"tiles\">");
        foreach (var t in tiles)
            sb.Append("<div class=\"tile\"><div class=\"num\">").Append(E(t.value))
              .Append("<span class=\"unit\">").Append(E(t.unit)).Append("</span></div>")
              .Append("<div class=\"tlabel\">").Append(E(t.label)).Append("</div>")
              .Append("<div class=\"tsub\">").Append(E(t.sub)).Append("</div></div>");
        sb.Append("</div></section>");
    }

    private static List<Tile> TimeTiles(PerfBenchResult r)
    {
        var tiles = new List<Tile>();
        var eng = r.Scenarios
            .Where(s => s.Kind == "engine" && s.Cold != null && s.Cold.ContainsKey("ingestMs"))
            .OrderByDescending(s => s.Rows).FirstOrDefault();
        if (eng?.Cold == null) return tiles;
        var c = eng.Cold;
        var sp = eng.Spread;
        string sub = RowsShort(eng.Rows) + " rows";
        if (c.TryGetValue("ingestMs", out var ing)) tiles.Add(TimeTile(ing, "Ingest", WithRange(sub, sp, "ingestMs")));
        if (c.TryGetValue("indexBuildMs", out var idx)) tiles.Add(TimeTile(idx, "Build search index", WithRange(sub, sp, "indexBuildMs")));
        if (c.TryGetValue("ingestMs", out var i2) && c.TryGetValue("indexBuildMs", out var x2))
            tiles.Add(TimeTile(i2 + x2, "Ready to search", sub));
        if (c.TryGetValue("searchWorstMs", out var w)) tiles.Add(TimeTile(w, "Search · matches all", sub));
        return tiles;
    }

    private static List<Tile> PagingTiles(PerfBenchResult r)
    {
        var tiles = new List<Tile>();
        var eng = r.Scenarios
            .Where(s => s.Kind == "engine" && s.Cold != null && s.Cold.ContainsKey("firstPage500Ms"))
            .OrderByDescending(s => s.Rows).FirstOrDefault();
        if (eng?.Cold == null) return tiles;
        var c = eng.Cold;
        string sub = RowsShort(eng.Rows) + "-row table";
        void Add(string key, string label) { if (c.TryGetValue(key, out var v)) tiles.Add(TimeTile(v, label, sub)); }
        Add("firstPage500Ms", "First page · 500");
        Add("firstPage1000Ms", "First page · 1,000");
        Add("firstPage5000Ms", "First page · 5,000");
        Add("jumpToLastMs", "Jump to last page");
        return tiles;
    }

    /// <summary>Append the median's min–max spread to a tile sub-line when the repeats disagreed —
    /// so a noisy run's hero number visibly carries its uncertainty.</summary>
    private static string WithRange(string sub, Dictionary<string, PerfBenchMinMax>? sp, string key)
        => sp != null && sp.TryGetValue(key, out var mm) && mm.Max > mm.Min
            ? sub + " · " + FmtRange(mm.Min, mm.Max)
            : sub;

    private static Tile TimeTile(double ms, string label, string sub)
    {
        var (v, u) = FmtTimeParts(ms);
        return new Tile(v, u, label, sub);
    }

    private static (string v, string u) FmtTimeParts(double ms)
        => ms >= 1000 ? ((ms / 1000.0).ToString("0.#", CultureInfo.InvariantCulture), "s") : (Num(ms), "ms");

    private static string FmtRange(double min, double max)
    {
        var (v1, u1) = FmtTimeParts(min);
        var (v2, u2) = FmtTimeParts(max);
        return u1 == u2 ? $"{v1}–{v2} {u1}" : $"{v1} {u1} – {v2} {u2}";
    }

    private static List<Tile> HeadlineTiles(PerfBenchResult r)
    {
        var tiles = new List<Tile>();
        // For each ratio kind, use the largest-size scenario that carries it.
        foreach (var key in new[] { "ftsVsScan", "usPerRow", "parallelSpeedup", "scopeSpeedup" })
        {
            var pick = r.Scenarios
                .Where(s => s.Ratios.ContainsKey(key))
                .OrderByDescending(s => s.Rows)
                .FirstOrDefault();
            if (pick == null) continue;
            double v = pick.Ratios[key];
            tiles.Add(new Tile(Num(v), RatioUnit(key), RatioLabel(key), $"at {RowsShort(pick.Rows)} rows"));
        }
        return tiles;
    }

    private static string RatioLabel(string key) => key switch
    {
        "ftsVsScan" => "FTS index vs. LIKE scan",
        "usPerRow" => "Ingest cost per row",
        "parallelSpeedup" => "Parallel ingest speed-up",
        "scopeSpeedup" => "Scoped-load speed-up",
        _ => key,
    };
    private static string RatioUnit(string key) => key switch
    {
        "ftsVsScan" or "parallelSpeedup" or "scopeSpeedup" => "×",
        "usPerRow" => "µs",
        _ => "",
    };

    // ---- small helpers ----

    private static void InfoCard(StringBuilder sb, string title, (string k, string v)[] rows)
    {
        sb.Append("<div class=\"card\"><h3>").Append(E(title)).Append("</h3><dl>");
        foreach (var (k, v) in rows)
            sb.Append("<dt>").Append(E(k)).Append("</dt><dd>").Append(E(string.IsNullOrEmpty(v) ? "—" : v)).Append("</dd>");
        sb.Append("</dl></div>");
    }

    private static string Ms(Dictionary<string, double> c, string key)
        => c.TryGetValue(key, out var v) ? "<span class=\"mono\">" + Num(v) + "</span>" : "<span class=\"muted\">—</span>";

    private static string RowsShort(long n) => n >= 1_000_000 ? $"{n / 1_000_000}M" : n >= 1000 ? $"{n / 1000}k" : n.ToString();

    private static string ShortDate(string iso)
    {
        return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var d)
            ? d.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : iso;
    }

    private static string Num(double v)
        => Math.Abs(v - Math.Round(v)) < 0.05
            ? Math.Round(v).ToString("N0", CultureInfo.InvariantCulture)
            : v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    // Warm-neutral surfaces, one indigo accent, validated light + dark. Numbers tabular + mono.
    private const string Css = @"
:root{
  --bg:#f4f2ee; --card:#fffdfa; --border:#e7e3da; --ink:#211e19; --muted:#77726a; --faint:#a49e93;
  --accent:#5b4bd6; --accent-weak:#efeaff; --ok:#1f8a5b; --skip:#b06a1a;
  --warn:#c67611; --warn-weak:#fdf1df;
  color-scheme:light dark;
}
@media(prefers-color-scheme:dark){:root{
  --bg:#141219; --card:#1c1a24; --border:#2c2836; --ink:#ece8f4; --muted:#9c96ab; --faint:#6a6478;
  --accent:#a394ff; --accent-weak:#241f39; --ok:#4cc38a; --skip:#e0a250;
  --warn:#e0a250; --warn-weak:#2a2012;
}}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);
  font:15px/1.55 ui-sans-serif,-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;
  -webkit-font-smoothing:antialiased}
main{max-width:900px;margin:0 auto;padding:3rem 1.5rem 4rem}
.mono{font-family:ui-monospace,'Cascadia Code','SF Mono',Consolas,monospace;font-variant-numeric:tabular-nums}
header{margin-bottom:2.5rem}
.eyebrow{color:var(--accent);font-weight:700;letter-spacing:.14em;text-transform:uppercase;font-size:.72rem}
h1{font-size:2rem;line-height:1.1;margin:.35rem 0 .5rem;letter-spacing:-.02em}
.meta{color:var(--muted);font-size:.88rem;margin:0;display:flex;align-items:center;flex-wrap:wrap;gap:.6rem}
.meta b{color:var(--ink)}
.dot{width:3px;height:3px;border-radius:50%;background:var(--faint);display:inline-block}
section{margin:2.2rem 0}
h2{font-size:.8rem;letter-spacing:.08em;text-transform:uppercase;color:var(--muted);
  margin:0 0 .9rem;display:flex;align-items:baseline;gap:.6rem}
.tag{font-size:.68rem;letter-spacing:.04em;color:var(--faint);text-transform:none}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:.9rem}
.tile{background:var(--card);border:1px solid var(--border);border-radius:14px;padding:1.1rem 1.2rem}
.num{font-size:2.1rem;font-weight:700;line-height:1;letter-spacing:-.02em;color:var(--accent);
  font-variant-numeric:tabular-nums}
.unit{font-size:1rem;font-weight:600;margin-left:.15rem;color:var(--accent);opacity:.7}
.tlabel{margin-top:.55rem;font-weight:600;font-size:.92rem}
.tsub{color:var(--muted);font-size:.8rem;margin-top:.1rem}
.twocol{display:grid;grid-template-columns:1fr 1fr;gap:.9rem}
@media(max-width:640px){.twocol{grid-template-columns:1fr}}
.card{background:var(--card);border:1px solid var(--border);border-radius:14px;padding:1.1rem 1.3rem}
.card h3{margin:0 0 .7rem;font-size:.78rem;letter-spacing:.06em;text-transform:uppercase;color:var(--muted)}
dl{display:grid;grid-template-columns:auto 1fr;gap:.4rem 1rem;margin:0}
dt{color:var(--muted);white-space:nowrap}
dd{margin:0;text-align:right;font-variant-numeric:tabular-nums}
table{width:100%;border-collapse:collapse;font-size:.9rem}
thead th{font-size:.72rem;letter-spacing:.04em;text-transform:uppercase;color:var(--muted);font-weight:600;
  text-align:right;padding:0 .7rem .55rem;border-bottom:1px solid var(--border)}
th.l{text-align:left}
tbody td{padding:.55rem .7rem;text-align:right;border-bottom:1px solid var(--border);font-variant-numeric:tabular-nums}
td.l{text-align:left}
tbody tr:last-child td{border-bottom:0}
.muted{color:var(--faint)}
.ok{color:var(--ok);font-size:.78rem;font-weight:600}
.skip{color:var(--skip);font-size:.78rem}
.warn{background:var(--warn-weak);border:1px solid var(--warn);border-radius:10px;padding:.65rem .95rem;
  font-size:.85rem;margin:1.2rem 0;color:var(--ink)}
.warn b{color:var(--warn)}
.notes{margin:0;padding-left:1.1rem;color:var(--muted);font-size:.88rem}
.notes li{margin:.2rem 0}
footer{margin-top:3rem;padding-top:1rem;border-top:1px solid var(--border);
  color:var(--faint);font-size:.78rem;display:flex;align-items:center;flex-wrap:wrap;gap:.5rem}
";
}
