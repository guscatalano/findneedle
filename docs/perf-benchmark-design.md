# FindNeedle Performance Benchmark — end-to-end design (draft)

Status: **v1 implemented.** Steps 1–6 built and shipping under Diagnostics → Performance benchmark
(`FindPluginCore/Diagnostics/PerfBench/*`, `FindNeedleUX/Pages/PerformanceBenchmarkPage`,
`tools/perfbench/aggregate.ps1`). v1 covers the **engine** scenarios (ingest / FTS-build / selective-vs-
worst search + parallel-vs-serial ingest); **viewer, decode, time-scope and storage-tier scenarios are
follow-on** (recorded in each result's `notes`). Goal: a built-in, reproducible benchmark that (a) the
author publishes as a reference run, and (b) anyone can run on their own machine and send back a result
file — a **community benchmark**. No phone-home: results are a file the user reviews and submits manually.

## 0. Principles (the decisions we've locked)

1. **No network / no phone-home.** The benchmark produces a file; the user reviews it and attaches it to a
   submission channel themselves. The app never uploads anything.
2. **Privacy is the enabling feature.** The result file contains **only** hardware specs, app/config, and
   timings — **zero log content, zero file paths, no username/hostname**. That's what makes people willing
   to send it.
3. **Ratios/curves are the cross-machine comparison; milliseconds are per-machine context.** Absolute ms
   only compare *within* a hardware class; ratios (FTS-vs-scan, parallel-vs-serial, scoped-vs-full, µs/row)
   compare across *any* machine. The report and the aggregation lead with ratios.
4. **Frozen, versioned scenario set.** Submissions are only comparable if everyone ran the identical
   benchmark. A `benchmarkVersion` gates comparability; changing the generator or scenarios bumps it.
5. **Deterministic, self-contained data — no admin, no external files.** Synthetic plain-text logs are
   generated from a fixed seed (byte-identical everywhere). One small **committed** real-trace fixture
   covers the decode path. Nothing needs ETW capture or the author's private captures.
6. **Median-of-N, not single-shot.** Every timed scenario runs N=3 and reports median + min/max, because
   wall-clock varies ±20% run-to-run (cache/AV/thermal). This is how we get "consistent."

## 1. Result artifact — the schema (the contract; lock this first)

One JSON file. `benchmarkVersion` freezes the scenario set. Everything (runner, export, aggregation) keys
off this. Ratios are first-class; ms live under `cold`/`warm` and are always labeled.

```jsonc
{
  "schema": "findneedle.perfbench/v1",
  "benchmarkVersion": 1,                 // frozen scenario set; only compare within a version
  "runId": "20260725-2231-<rand>",       // opaque, not PII
  "timestampUtc": "2026-07-25T22:31:04Z",
  "durationOfRunSec": 74,

  "app":    { "version": "1.0.210.0", "gitSha": "d9737d3", "configuration": "Release",
              "runtime": ".NET 8.0.x", "arch": "x64" },

  "machine": {                           // hardware only — NO hostname/username/paths
    "cpuModel": "AMD Ryzen 7 7840U", "logicalCores": 16, "physicalCores": 8,
    "ramGB": 61.8, "os": "Windows 11 (10.0.26200)",
    "diskType": "NVMe|SSD|HDD|Unknown", "onBattery": false
  },

  "config": {                            // knobs that change results, recorded for apples-to-apples
    "storageTier": "auto", "parallelIngest": true, "ftsEnabled": true,
    "backgroundIndex": true, "pageSize": 5000
  },

  "systemLoad": {                        // so a busy machine's inflated ms are interpretable
    "idleCpuPercentBefore": 4.2,         // system-wide CPU sampled over ~1–2 s BEFORE the run
    "peakForeignCpuPercentDuring": 18.0, // non-FindNeedle CPU seen during the run (best-effort)
    "availableRamGB": 40.1,              // free RAM at start
    "wdkPresent": true                   // tracefmt/WDK available → WPP decode scenario ran
  },

  "preset": "quick|full|full+10m",
  "repeats": 3,                          // median-of-N

  "scenarios": [
    {
      "id": "engine.text.1M", "kind": "engine",
      "dataset": "synthetic-log", "datasetVersion": 1, "rows": 1000000,
      "storageTierChosen": "Sqlite",
      "cold": { "ingestMs": 8200, "indexBuildMs": 3100, "totalToReadyMs": 11300,
                "searchSelectiveMs": 40, "searchWorstMs": 620 },
      "warm": { "reopenMs": 300, "searchSelectiveMs": 12 },
      "ratios": { "ftsVsScan": 22.8, "usPerRow": 9.6 },
      "spread": { "ingestMs": { "min": 8010, "max": 8600 } }   // min/max across the N repeats
    },
    { "id": "ingest.parallel.5M", "kind": "engine", "rows": 5000003,
      "serialMs": 51700, "parallelMs": 40400, "ratios": { "parallelSpeedup": 1.28 } },
    { "id": "scope.text.5M", "kind": "engine", "rows": 5000000,
      "fullMs": 38000, "scopedMs": 12000, "keptRows": 500000,
      "ratios": { "scopeSpeedup": 3.1, "keptFraction": 0.10 } },
    { "id": "decode.selfdescribing.small", "kind": "decode",
      "dataset": "committed-etl-fixture", "datasetVersion": 1, "rows": 200000,
      "status": "ok|skipped", "skipReason": null,
      "decodeMs": 1900, "ingestMs": 900, "indexBuildMs": 700 },
    { "id": "viewer.5M", "kind": "viewer", "rows": 5000000,
      "firstPageMs": 500, "jumpToLastMs": 1700,
      "inputRoundTripMs": { "median": 3, "p95": 6, "max": 9 } }
  ],

  "primaryMetrics": ["ftsVsScan", "parallelSpeedup", "scopeSpeedup", "usPerRow"],
  "notes": []                            // e.g. "decode skipped: tracefmt/WDK not found"
}
```

Schema rules:
- **No PII fields, ever.** No hostname, no user folder, no source paths. `runId` is opaque.
- `status`/`skipReason` on every scenario so partial runs (e.g. no WDK → WPP decode skipped) are still
  aggregatable and the author knows what's missing.
- **`systemLoad` makes noisy runs honest.** If someone runs it with a dozen apps open, `idleCpuPercentBefore`
  will be high and the absolute ms are inflated — the report shows this, the UI warns, and the author can
  filter or discount those submissions. (Ratios survive a busy machine far better than absolute ms — another
  reason ratios lead.)

### 1a. Outputs — JSON (submission) + HTML (human / publishable)

The run produces **two files from the one result object**:
- **`…​.json`** — the machine-readable submission (§1). This is what users send back.
- **`…​.html`** — a **self-contained** report (inline CSS/JS, inline SVG charts, **no external/CDN
  dependencies** — same constraint as the existing `docs/findneedle-performance-whitepaper.html`). Ratios
  and scaling curves up top as inline-SVG charts, the machine card, then absolute ms as "on this machine,"
  with the author's bundled reference overlaid for comparison.
- (Optional) a short **Markdown** blob behind "Copy summary" for pasting into an issue.

The HTML renderer takes the result object and emits a string — a sibling to
`FindPluginCore/Diagnostics/PerformanceReport.cs` (which already emits machine specs + Markdown). Crucially,
**the author's published report is generated by this same renderer from the reference JSON**, so the
published HTML can never drift from the numbers (this replaces the hand-maintained whitepaper).

## 2. Scenario set (benchmarkVersion = 1)

Two presets so the user picks their time/space budget:

| Preset | Scenarios | ~Time | ~Temp disk |
|---|---|---|---|
| **Quick** (default) | engine.text.100k, engine.text.1M, decode.selfdescribing (+ decode.wpp if WDK), viewer.1M | ~20–40 s | ~70 MB |
| **Full** | + engine.text.5M, ingest.parallel.5M, scope.text.5M, viewer.5M | ~1–2 min | ~350 MB |
| **Full + 10M** (opt-in) | + engine.text.10M, ingest.parallel.10M, viewer.10M | ~3–5 min | ~700 MB |

The 10M tier is an explicit opt-in (checkbox within Full) for beefy machines — gated behind a spec check
and a disk-space check, with a clear "~700 MB, several minutes" warning.

What each measures (and which metric is primary):

| Scenario | Measures | Primary (ratio) | Secondary (ms) |
|---|---|---|---|
| `engine.text.{100k,1M,5M}` | ingest, FTS build, tier chosen, selective vs worst-case search; cold + warm | **FTS-vs-scan**, **µs/row** | ingest, index, search, reopen |
| `ingest.parallel.5M` | serial vs parallel ingest (toggles `ParallelIngestEnabled`, reset after) | **parallelSpeedup** | serial/parallel ms |
| `scope.text.5M` | full vs time-window scoped load, kept fraction | **scopeSpeedup** | full/scoped ms |
| `decode.selfdescribing` | decode a committed self-describing ETL (no WDK) → ingest → index | (none) | **decodeMs**, ingest, index |
| `decode.wpp` *(if WDK)* | decode a committed WPP ETL via tracefmt; skipped+noted if no WDK | (none) | **decodeMs**, ingest, index |
| `viewer.{1M,5M}` | first page, jump-to-last, input round-trip latency | (none — "stays interactive") | first-page, jump, round-trip |

Notes:
- The tier-crossover story falls out for free: 100k → Hybrid, 1M/5M → SQLite+FTS, so one run exercises all
  three backends and records `storageTierChosen`.
- `engine` scenarios plant a **rare token** (selective query) and rely on **common tokens** (worst-case
  "matches ~everything") so both search modes are measured from the same dataset — this is the fix for
  "search latency depends on term selectivity": we report *both*, labeled.

## 3. Synthetic data generator (shippable, deterministic)

- New shippable generator (port the deterministic logic from the test-only
  `ETWPluginTests/LargeFixtureGenerator.cs`) in `FindPluginCore` or `FindNeedleCoreUtils` — **not** a test
  project.
- **Fixed seed → byte-identical output on every machine.** Line shape:
  `‹ISO-8601 ts› ‹level› ‹provider› ‹task› ‹message›`, timestamps spanning a fixed window (so the scope
  scenario deterministically keeps ~10%). Message tokens drawn from a fixed vocabulary of **common** words
  plus a **rare** `NEEDLE_‹k›` planted at a known low rate (e.g. 1 in 50k) for the selective query.
- Writes to a temp dir under the scratch/temp area; **cleaned up in `finally`**. `datasetVersion` bumps if
  the generator changes.
- No elevation (plain file writes), no ETW.

## 4. The committed real-decode fixtures (both paths)

Two small **committed** `.etl` fixtures (tracked path e.g. `Samples/perfbench/`, **not** the gitignored
`LargeSamples/`), byte-stable → deterministic decode:

1. **`decode.selfdescribing.small`** — a self-describing (EventSource/TraceLogging) ETL. **Verified: decodes
   with no WDK on any machine.** `ETLProcessor.LooksLikeModernTrace` (`ETLProcessor.cs:563`) probes the first
   20k events via the managed `ETWTraceEventSource` (TraceEvent library, shipped with the app); when <5% are
   unparseable it routes to `DecodeWithTraceEvent` (`:305`), bypassing tracefmt. So **every** submitter gets
   this decode number.
2. **`decode.wpp.small`** — a WPP ETL (self-resolving via embedded TMF, à la `tools/WppEmitter`) that
   exercises the tracefmt path. Requires **WDK/tracefmt**; when absent it **runs `status:"skipped"`,
   `skipReason:"tracefmt/WDK not found"`**, `systemLoad.wdkPresent:false`, and a line in `notes[]` — so a
   submission without it is still valid and the author knows why.

So: everyone (ideally) gets the self-describing decode number; machines with the WDK additionally get the WPP
decode number; and the report **always states whether WDK was present**.

## 5. The runner

- Runs **in-process, on a background thread**, sequential (`DoNotParallelize` semantics), cancellable,
  progress reported per scenario. Cleans up temp files on completion/failure.
- **Engine/decode:** drive the real core (`NuSearchQuery.RunThrough` over a temp folder location), read
  `PerfReport.Last` after each run for phase timings. Between scenarios, **reset the process-global flags**
  (`ParallelIngestEnabled`, `SqliteStorage.DisableFtsForMeasurement`, `DeferIndexBuild`, `FtsShardThreshold`)
  to known values, and control cache explicitly (`CacheReuseMode.Never` for the cold pass, then reopen for
  the warm pass). A leaked flag would poison the numbers — the runner owns them.
- **Viewer:** drive the real native viewer in-process (this is the reason it's an in-app feature, not a
  console). Measure `viewer.native.*` phases for first-page/jump, and **input round-trip latency via the
  existing `UxMonitor` probe** (`FindNeedleUX/Services/Diagnostics/UxMonitor.cs`) — real interaction
  latency (scroll/filter round-trips), reported as median/p95/max. This "stays interactive on N million
  rows" number is the differentiator, so it's captured for real, not proxied.
- **Median-of-N:** each timed measurement runs `repeats` (default 3); report median + min/max in `spread`.
- Machine specs from `PerformanceReport`'s existing collectors (CPU registry, `GlobalMemoryStatusEx`, OS).

## 6. In-app UX — Diagnostics → "Performance benchmark…"

**Navigation:** a general (non-dev) item in the **Diagnostics** menu, next to *System check / Logs /
Search statistics / WPP symbol resolution*. It pairs with **Search statistics** — that page explains
*your last search*, this one measures *your machine* on a standard workload. Wired like the other pages:
a `<MenuFlyoutItem Name="perf_benchmark">` in `MainWindow.xaml` + a `case "perf_benchmark"` navigating to
`PerformanceBenchmarkPage`.


1. **Intro + consent** (must accept before running): "This generates up to ~350 MB of synthetic logs in a
   temp folder and runs the full pipeline — expect heavy CPU/disk for ~1–2 min. Temp files are deleted
   afterward. Your result file contains only hardware specs and timings — **no log data**."
2. **Machine card** shown *before* running (so the user sees exactly what will be in the file).
3. **Preset** (Quick / Full, + a "include 10M" opt-in) with est. time + temp size; battery warning if on
   battery. **Baseline-load check:** sample system-wide CPU for ~1–2 s before running; if it's high (e.g.
   >15%), warn "other apps are using N% CPU — your absolute times will be inflated; close them for cleaner
   numbers (ratios are less affected)." The measured `idleCpuPercentBefore` goes into the result either way.
4. **Run** → per-scenario progress, cancellable.
5. **Results** — ratios highlighted first (with a tiny scaling curve), ms shown as "on this machine";
   **"Compare to reference"** overlays the author's bundled reference numbers so the user immediately sees
   how their box stacks up.
6. **Export**: **Open HTML report** (the self-contained `.html`, §1a — the thing to read/share) · **Save
   JSON** (the submission) · **Copy summary** · one-line "Attach the JSON to ‹channel›."

## 7. Submission flow (manual, transparent)

- No upload. The user saves `findneedle-perfbench-v1-‹date›.json`, glances at it (hardware + numbers), and
  attaches it to a **GitHub Discussion/Issue with a template** (recommended over email: public, greppable,
  transparent). The template asks for the attached JSON and nothing else identifying.

## 8. Author side — publish + aggregate

- The published whitepaper/README numbers are **generated from the author's own reference JSON** with the
  same renderer → self-consistent (kills the current 8.2×/8.4× hand-transcription drift).
- A small script (`tools/perfbench/aggregate.*`) ingests a folder of submitted JSONs, filters by
  `benchmarkVersion`, and renders:
  - **Ratio charts across all submissions** regardless of hardware (parallelSpeedup, ftsVsScan,
    scopeSpeedup, µs/row) — the headline, hardware-invariant.
  - **Absolute ms bucketed by hardware class** (cores / RAM / disk type).
  - A scatter/leaderboard (e.g. cores vs 5M ingest time) for fun and capacity guidance.

## 9. Decisions

**Resolved:**
- ✅ **Viewer latency** — full input round-trip via `UxMonitor` (median/p95/max). Not proxied.
- ✅ **Max size** — offer **10M** as an explicit opt-in within Full (~700 MB, gated by spec + disk check).
- ✅ **Report format** — self-contained **HTML** is the human/publishable output (plus the JSON submission);
  the author's published report is generated from the reference JSON by the same renderer.
- ✅ **Decode fixtures** — ship **both** (§4): a self-describing ETL (universal, no WDK) and a WPP ETL
  (WDK-only); when WDK is absent the WPP scenario is **skipped and noted** (`wdkPresent:false`, `notes[]`).
- ✅ **System load** — capture idle/baseline CPU + free RAM before the run so busy-machine numbers are
  interpretable (§1 `systemLoad`, §6 warning).
- ✅ **Submission channel** — a pinned **GitHub Discussion** ("Benchmark submissions"); the app's Submit
  button deep-links to it; users attach their JSON as a comment. No upload.
- ✅ **Bundled reference** — ship the author's reference `perfbench.json` in-app so a user's first run shows
  "you vs the reference machine" immediately.

- ✅ **Decode path (verified in code)** — a self-describing ETL decodes with **no WDK** on every machine
  (`ETLProcessor.LooksLikeModernTrace` → `DecodeWithTraceEvent`, `ETLProcessor.cs:305/563`). So the run does
  **both** decode scenarios when WDK is available (`decode.selfdescribing` + `decode.wpp`), and just
  `decode.selfdescribing` otherwise — every submitter gets at least one decode number.

**All decisions resolved — design is locked.** Ready to build per §10.

## 10. Build order

1. **Lock this schema (§1).**
2. Deterministic generator (§3) + commit the decode fixture (§4).
3. Runner (§5) with flag-reset + median-of-N.
4. **Result → HTML renderer** (§1a, self-contained, inline-SVG charts) + JSON writer.
5. In-app page (§6): consent → run → results → Open HTML / Save JSON.
6. Aggregation script (§8) + generate the author's published HTML from the reference JSON.
