# Decode-scoping plan (deferred)

Status: **planned, not started.** Parked while we continue the profiling work. Grounded in the
decode CPU profiles captured 2026-07 (see `WorkloadProfiler` / `ETWPluginTests/DecodeProfileTests`).

## Goal
Make "only load the providers / time window I care about" actually skip the work for everything else,
so opening a huge (esp. WPP) capture is a fraction of the full-load cost.

## What already exists (don't rebuild)
The earlier "per-instance ETLProcessor" gotcha is **already resolved** — scoping is an ambient
`DecodeScope`, fully wired:

- **Filter:** `FindNeedlePluginLib.DecodeScope.Keep(provider, timestamp, level)` — tested (`CoreTests/DecodeScopeKeepTests`).
- **Source:** a `scope` RuleDSL rule (`purpose == "scope"`) → `NuSearchQuery.ResolveDecodeScope()`
  (`FindPluginCore/Searching/NuSearchQuery.cs:274`) → ambient `DecodeScope.Current` set at `:776`, cleared at `:1063`.
- **Triage UI:** `TriageService.WriteScopeRuleFile(selected)` → `MiddleLayerService.PendingScopeRulePath`
  (`FindNeedleUX/MainWindow.xaml.cs:1906`). Inspect providers → pick → scope the load is a real button.
- **Honored in the modern decode:** `ETWPlugin/FileExtension/ETLProcessor.cs:634` (`DecodeWithTraceEvent`).
- **Cache:** scoped loads get a distinct cache DB (`ScopeCacheSuffix`, NuSearchQuery `:290`).

So for **modern** traces (manifest / EventSource / TraceLogging / kernel — incl. `cats-wpp.etl`, which
decodes via TraceEvent) scoping works today.

## The profiles that drive this (FULL decode: OpenFile + DoPreProcessing + LoadInMemory + GetResults)
NOTE: `DoPreProcessing` *defers* the decode for modern traces, so an earlier "decode profile" that only
ran OpenFile+DoPreProcessing measured the **detection pre-scan**, not the decode. These numbers force the
real decode via `LoadInMemory`. They differ by trace kind, and that difference is the whole story:

- **Manifest/TDH** (1.28 GB multi-provider): **~90% `TraceEventNativeMethods.ProcessTrace`** (native
  buffer parse), only ~1% our `Handle` callback, ~5% .NET string/JSON field formatting, <1%
  `TryLookupWorker`. Decode here is **native-bound**: the per-event *managed* work our scope filter can
  skip is tiny.
- **WPP** (`cats-wpp.etl`, TraceEvent route): **~72% `TraceEventDispatcher.Insert` + ~13%
  `TryLookupWorker`** (per-event clone + time-ordered dispatch queue + schema lookup), ~8% `ReadFile`,
  only ~3% `ProcessTrace`. Decode here is **per-event managed-bound**, so it scales ~linearly with the
  number of events dispatched.

Key realizations:
1. The current `scope.Keep` runs **inside the per-event callback**, *after* TraceEvent has already done
   its work. So it only skips **our** downstream (ETLLogLine wrap + storage ingest + FTS). That downstream
   is what made the triage prototype ~8.4× end-to-end.
2. **Scoping's effect on decode CPU is workload-dependent.** On **manifest** traces decode is ~90% native
   parse, so callback-level scoping barely dents *decode* (the win is purely downstream ingest+FTS). On
   **WPP** traces ~85% is per-event managed dispatch, so cutting dispatched events shrinks *decode itself*
   ~linearly — scoping is far more valuable exactly on the WPP traces we care about.

## The two real gaps

### Gap 1 — the legacy tracefmt/WPP path ignores `DecodeScope` (highest value, lowest risk)
`scope.Keep` exists only in `DecodeWithTraceEvent`. A pure-classic-WPP `.etl` that fails
`LooksLikeModernTrace` (`ETLProcessor.cs:305`) goes down the tracefmt line-parse loop
(`ETLProcessor.cs:490–501`) and wraps + emits **every** line regardless of scope.

**Fix:** between `etlline.PreLoad()` (`:491`) and `emit(etlline)` (`:501`), add
`if (DecodeScope.Current is {} sc && !sc.Keep(etlline.GetSource(), etlline.GetLogTime(), -1)) continue;`
(mirrors `:634`). Skips the wrap + ingest + FTS for out-of-scope WPP lines. Cannot scope tracefmt.exe's
own decode (black box), but the downstream is the bigger end-to-end cost. Add a scope test mirroring
`ETWPluginTests/TriageScopeTests`.

### Gap 2 — filter runs too late to cut the dominant *decode* cost
The callback-level `scope.Keep` runs after TraceEvent's per-event work, so it can't shrink the decode
itself. This matters most for **WPP** (the ~85% per-event `Insert`/`Lookup`); for **manifest** traces the
decode is ~90% native `ProcessTrace` that even earlier filtering can't avoid (the native loop reads all
buffers regardless), so there Gap 2 mainly saves the ~5% managed formatting.

- **2a. Time-window early-out (small, safe, helps both):** events dispatch in timestamp order, so once past
  the scope's end time, call `source.StopProcessing()` in `DecodeWithTraceEvent` — skips reading the rest
  of a long capture entirely. Big win for time-scoped loads on either trace kind.
- **2b. Provider-scoped subscription (biggest WPP decode-CPU win, most nuanced):** register callbacks only
  for the wanted provider GUIDs instead of the `source.Dynamic.All += Handle` catch-all
  (`ETLProcessor.cs:656`), so TraceEvent doesn't clone/insert/dispatch unwanted events — directly attacks
  the ~72% `Insert` on WPP-heavy captures. Less impactful on manifest traces (native `ProcessTrace`
  dominates there). Watch TraceEvent's GUID-subscription semantics for file sources.

## Proposed order
1. Gap 1 — tracefmt path honors `DecodeScope` + test.
2. Gap 2a — time early-out via `StopProcessing`.
3. Gap 2b — provider-scoped subscription.
4. Re-profile with a scope active (reuse `WorkloadProfiler.ProfileAction`) to confirm decode CPU drops.

## Notes / caveats
- `cats-wpp.etl` decodes via the TraceEvent path (not tracefmt), so testing Gap 1 needs a pure-classic-WPP
  `.etl` that fails `LooksLikeModernTrace` — may need a fixture.
- `DecodeScope.Keep`'s "unknown dimension is not filtered" contract (null provider/timestamp/level < 0
  skip their check) must be preserved on the new call sites — see `DecodeScopeKeepTests`.
