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
/// author's published report can never drift from the numbers. Ratios lead; milliseconds are labeled
/// "on this machine."
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
        sb.Append("<style>").Append(Css).Append("</style></head><body>");

        sb.Append("<h1>FindNeedle Performance Benchmark</h1>");
        sb.Append("<p class=\"sub\">")
          .Append("benchmark v").Append(r.BenchmarkVersion)
          .Append(" · preset <b>").Append(E(r.Preset)).Append("</b>")
          .Append(" · median of ").Append(r.Repeats)
          .Append(" · ").Append(E(r.TimestampUtc))
          .Append(" · run ").Append(E(r.RunId))
          .Append("</p>");

        // Machine + load
        sb.Append("<div class=\"cards\">");
        Card(sb, "Machine", new (string, string)[]
        {
            ("CPU", r.Machine.CpuModel),
            ("Cores", r.Machine.LogicalCores.ToString()),
            ("RAM", Num(r.Machine.RamGB) + " GB"),
            ("OS", r.Machine.Os),
            ("Arch", r.App.Arch),
        });
        Card(sb, "Run context", new (string, string)[]
        {
            ("App", string.IsNullOrEmpty(r.App.Version) ? "(dev)" : r.App.Version + " " + r.App.Configuration),
            ("Runtime", r.App.Runtime),
            ("Idle CPU before", Num(r.SystemLoad.IdleCpuPercentBefore) + " %"),
            ("Free RAM", Num(r.SystemLoad.AvailableRamGB) + " GB"),
            ("WDK (WPP decode)", r.SystemLoad.WdkPresent ? "present" : "absent"),
        });
        sb.Append("</div>");

        // Headline ratios (cross-machine)
        var ratioRows = r.Scenarios
            .SelectMany(s => s.Ratios.Select(kv => (s.Id, kv.Key, kv.Value)))
            .ToList();
        if (ratioRows.Count > 0)
        {
            sb.Append("<h2>Headline ratios <span class=\"hint\">— compare across machines</span></h2>");
            double max = ratioRows.Max(x => x.Value);
            if (max <= 0) max = 1;
            sb.Append("<table class=\"ratios\">");
            foreach (var (id, key, val) in ratioRows)
            {
                sb.Append("<tr><td class=\"k\">").Append(E(RatioLabel(key)))
                  .Append("</td><td class=\"scn\">").Append(E(id))
                  .Append("</td><td class=\"barcell\"><div class=\"bar\" style=\"width:")
                  .Append((Math.Min(val, max) / max * 100).ToString("F0", CultureInfo.InvariantCulture))
                  .Append("%\"></div></td><td class=\"v\">").Append(Num(val)).Append(RatioSuffix(key))
                  .Append("</td></tr>");
            }
            sb.Append("</table>");
        }

        // Per-scenario milliseconds (on this machine)
        sb.Append("<h2>Timings <span class=\"hint\">— on this machine (ms)</span></h2>");
        sb.Append("<table class=\"scenarios\"><thead><tr>")
          .Append("<th>Scenario</th><th>Rows</th><th>Tier</th><th>Ingest</th><th>Index build</th>")
          .Append("<th>Search (selective)</th><th>Search (worst)</th><th>Status</th></tr></thead><tbody>");
        foreach (var s in r.Scenarios)
        {
            var c = s.Cold ?? new Dictionary<string, double>();
            sb.Append("<tr><td>").Append(E(s.Id)).Append("</td><td>").Append(s.Rows.ToString("N0", CultureInfo.InvariantCulture))
              .Append("</td><td>").Append(E(s.StorageTierChosen ?? "—"))
              .Append("</td><td>").Append(MsCell(c, "ingestMs"))
              .Append("</td><td>").Append(MsCell(c, "indexBuildMs"))
              .Append("</td><td>").Append(MsCell(c, "searchSelectiveMs"))
              .Append("</td><td>").Append(MsCell(c, "searchWorstMs"))
              .Append("</td><td>").Append(s.Status == "ok" ? "✓" : "⤳ " + E(s.SkipReason ?? "skipped"))
              .Append("</td></tr>");
        }
        sb.Append("</tbody></table>");

        if (r.Notes.Count > 0)
        {
            sb.Append("<h2>Notes</h2><ul class=\"notes\">");
            foreach (var n in r.Notes) sb.Append("<li>").Append(E(n)).Append("</li>");
            sb.Append("</ul>");
        }

        sb.Append("<p class=\"foot\">Ratios (FTS-vs-scan, µs/row, …) compare across any hardware; ")
          .Append("absolute milliseconds are specific to this machine and its current load. ")
          .Append("Generated by FindNeedle.</p>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    // ---- helpers ----

    private static void Card(StringBuilder sb, string title, (string k, string v)[] rows)
    {
        sb.Append("<div class=\"card\"><h3>").Append(E(title)).Append("</h3><dl>");
        foreach (var (k, v) in rows)
            sb.Append("<dt>").Append(E(k)).Append("</dt><dd>").Append(E(string.IsNullOrEmpty(v) ? "—" : v)).Append("</dd>");
        sb.Append("</dl></div>");
    }

    private static string MsCell(Dictionary<string, double> c, string key)
        => c.TryGetValue(key, out var v) ? Num(v) : "—";

    private static string RatioLabel(string key) => key switch
    {
        "ftsVsScan" => "FTS vs LIKE scan",
        "usPerRow" => "Ingest µs/row",
        "parallelSpeedup" => "Parallel ingest speed-up",
        "scopeSpeedup" => "Scoped-load speed-up",
        _ => key,
    };
    private static string RatioSuffix(string key) => key switch
    {
        "ftsVsScan" or "parallelSpeedup" or "scopeSpeedup" => "×",
        "usPerRow" => " µs",
        _ => "",
    };

    private static string Num(double v)
        => (Math.Abs(v - Math.Round(v)) < 0.05 ? Math.Round(v).ToString("N0", CultureInfo.InvariantCulture)
                                               : v.ToString("0.##", CultureInfo.InvariantCulture));
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");

    private const string Css = @"
:root{color-scheme:light dark}
body{font:14px/1.5 -apple-system,Segoe UI,Roboto,sans-serif;max-width:960px;margin:2rem auto;padding:0 1rem;color:#1a1a1a;background:#fff}
@media(prefers-color-scheme:dark){body{color:#e6e6e6;background:#151515}.card,table{background:#1e1e1e;border-color:#333}th{background:#242424}.bar{background:#4aa3ff}}
h1{font-size:1.5rem;margin:.2rem 0}.sub{color:#888;margin:.2rem 0 1.2rem}.hint{color:#888;font-weight:400;font-size:.85rem}
.cards{display:flex;gap:1rem;flex-wrap:wrap;margin:1rem 0}
.card{flex:1 1 300px;border:1px solid #e2e2e2;border-radius:8px;padding:.8rem 1rem;background:#fafafa}
.card h3{margin:.1rem 0 .5rem;font-size:.95rem}
dl{display:grid;grid-template-columns:auto 1fr;gap:.15rem .8rem;margin:0}
dt{color:#888}dd{margin:0;text-align:right;font-variant-numeric:tabular-nums}
h2{font-size:1.1rem;margin:1.6rem 0 .5rem;border-bottom:1px solid #e2e2e2;padding-bottom:.2rem}
table{width:100%;border-collapse:collapse;font-variant-numeric:tabular-nums}
th,td{padding:.35rem .5rem;border-bottom:1px solid #eee;text-align:right}
th:first-child,td:first-child,.scn,.k{text-align:left}
.ratios .k{width:16rem}.ratios .scn{color:#888;font-size:.85rem}.ratios .v{font-weight:600;width:5rem}
.barcell{width:40%}.bar{height:12px;border-radius:6px;background:#2b7fff;min-width:2px}
.notes{color:#666}.foot{color:#999;font-size:.85rem;margin-top:2rem;border-top:1px solid #eee;padding-top:.6rem}
";
}
