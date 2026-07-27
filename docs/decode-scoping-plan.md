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

## The profiles that drive this (decode-only: OpenFile + DoPreProcessing)
- **Manifest/TDH** (1.28 GB multi-provider): ~74% `TraceEventNativeMethods.ProcessTrace` (native file
  walk + dispatch) + ~14% `RegisteredTraceEventParser.TryLookupWorker` (per-event schema lookup).
- **WPP** (`cats-wpp.etl`, TraceEvent route): ~77% `TraceEventDispatcher.Insert` (per-event clone +
  time-ordered dispatch queue) — a **per-event** cost, so cutting events shrinks it ~linearly.

Key realization: the current `scope.Keep` runs **inside the per-event callback**, i.e. *after* TraceEvent
has already done the 74–77% (ProcessTrace / Insert). So it only skips **our** downstream (ETLLogLine wrap
+ storage ingest + FTS). That downstream is what made the earlier triage prototype ~8.4× end-to-end — but
the decode CPU itself needs the filter pushed **earlier** to shrink.

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

### Gap 2 — filter runs too late to cut the dominant decode cost
To shrink the 74–77% TraceEvent-internal cost (not just our downstream):

- **2a. Time-window early-out (small, safe):** events dispatch in timestamp order, so once past the
  scope's end time, call `source.StopProcessing()` in `DecodeWithTraceEvent` — skips reading the rest of
  a long capture. Big win for time-scoped loads.
- **2b. Provider-scoped subscription (biggest decode-CPU win, most nuanced):** register callbacks only for
  the wanted provider GUIDs instead of the `source.Dynamic.All += Handle` catch-all
  (`ETLProcessor.cs:656`), so TraceEvent never dispatches/queues unwanted events — directly attacks the
  77% `Insert` on WPP-heavy captures. Watch TraceEvent's GUID-subscription semantics for file sources.

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
