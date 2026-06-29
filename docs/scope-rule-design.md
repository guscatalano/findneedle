# Design spec: `scope` rules — pre-decode load scoping for large captures

Status: **prototype / proposed.** Author: perf work, June 2026.

## 1. Motivation

Opening a multi-GB capture is minutes of work, and it is **O(n) in the rows actually ingested** — you
cannot meaningfully speed up the full load (the per-row decode→wrap→insert is the floor, and the parallel
fan-out merge even backfires past the low tens of millions of rows; see the performance whitepaper §8–9).

Measured on a real 36 M-event / 3.7 GB capture (≈90 % kernel events):

| Load | Time | Rows |
|---|---:|---:|
| Full | 409 s | 36.0 M |
| Scoped to the 24 non-kernel providers | **49 s** | 2.8 M |

So the only large lever is **not loading what you don't need**. The user often wants just the application
/ .NET / Defender events, a single time window, or only enough to render a UML — not the kernel scheduling
firehose. This spec makes that a first-class, declarative capability.

## 2. Concept

A new RuleDSL section **purpose: `scope`** that is evaluated **before everything else — at decode time,
before each event is wrapped into an `ISearchResult` and inserted into storage.** Events outside the scope
are never wrapped, never stored, never indexed. This is the declarative form of the prototype's
decode-time provider filter; it reuses RuleDSL's vocabulary instead of a bolt-on side channel.

```
file → decode raw event ──[ scope rule: keep? ]──► wrap (ETLLogLine) → filter/enrich → store → index
                                  │ no
                                  └─► dropped (never wrapped, never stored)
```

## 3. The hard constraint — what a `scope` rule may test

A scope rule runs *before the wrap*, so it can only reference fields that exist **without** wrapping the
event (the wrap — `PayloadStringByName`, message rendering — is the very cost we are skipping). Available
pre-wrap, cheaply, from the raw decoder event:

- **provider** (name)
- **timestamp**
- **level**
- **event name / id**

A scope rule may **not** test `message`, extracted fields, or payload — computing those *is* the wrap.
The loader rejects a `scope` section that references them (see §6). Regular `filter` rules (which can test
anything) keep working unchanged as a reversible **view** filter on loaded rows; `scope` is the opposite —
a one-way **load** decision.

| | `filter` rule (today) | `scope` rule (this spec) |
|---|---|---|
| Runs | after load, on stored rows | at decode, before wrap/insert |
| Fields | any (message, fields, …) | provider / time / level / event-name only |
| Effect on load | none (full load still happens) | skips load of dropped events (constant memory) |
| Reversible | yes (toggle the view) | no (dropped events aren't in the session) |

## 4. Schema

A scope section reuses the existing `UnifiedRuleSection.providers` list and adds a small, optional time/level
window. It is marked by a single rule with `action.type = "scope"` (consistent with how other purposes are
recognized by their action types):

```jsonc
{
  "sections": [
    {
      "name": "App + .NET only",
      "providers": [                      // allow-list; empty = all providers
        "Microsoft-Windows-DotNETRuntime",
        "Microsoft-Antimalware-Engine",
        "Microsoft-Windows-RPC"
      ],
      "rules": [
        {
          "name": "scope",
          "action": {
            "type": "scope",             // marks this section's purpose as pre-decode scope
            "timeFrom": "2025-07-26T08:00:00Z",   // optional ISO-8601 UTC; omit for open-ended
            "timeTo":   "2025-07-26T10:00:00Z",   // optional
            "levels":   ["Error", "Warning", "Information"], // optional; omit = all levels
            "providerMode": "include"    // "include" (default) or "exclude" the listed providers
          }
        }
      ]
    }
  ]
}
```

Notes:
- `providers` empty + only a time window ⇒ "all providers, this window".
- `providerMode: "exclude"` flips it to a drop-list (e.g. drop `Windows Kernel`, `MSNT_SystemTrace`), which
  is how the triage panel's "drop the kernel firehose" choice is expressed.
- New `UnifiedRuleAction` fields: `timeFrom`, `timeTo`, `levels`, `providerMode` (all optional, additive —
  existing rule files are unaffected).

## 5. Recognition

`scope` joins the existing purposes (`enrichment` / `filter` / `output` / `uml`). `GetSectionsByPurpose(rules,
"scope")` returns sections whose rules include an `action.type == "scope"`. A rules file may contain at most
one effective scope (multiple scope sections are AND-combined: an event must satisfy every scope section).

## 6. Loader validation

When rules load, each `scope` section is validated to be **pushdownable**:
- Its providers/time/level/event-name predicates are allowed.
- Any `match`/`unmatch`/`field` referencing `message` or an extracted field ⇒ **error** ("a scope rule can
  only test provider, time, level, or event name; `message` is not available before decode").
- Invalid scope ⇒ the load fails loudly with that message (don't silently run it as a slow view filter).

This is the guard that keeps "before everything" honest.

## 7. Evaluation

A compiled `ScopeRule` is built once from the scope sections:

```csharp
sealed class ScopeRule {
    HashSet<string>? IncludeProviders;     // null/empty = all
    HashSet<string>? ExcludeProviders;
    DateTime? FromUtc, ToUtc;
    HashSet<int>? Levels;                  // (int)Level
    bool Keep(string provider, DateTime tsUtc, int level); // O(1): hash lookups + compares
    static ScopeRule? FromSections(IReadOnlyList<UnifiedRuleSection>);
    static IReadOnlyList<string> Validate(IReadOnlyList<UnifiedRuleSection>); // errors, empty = ok
}
```

`Keep` must be **O(1)** (hash lookups + a couple of compares) — it runs per raw event (tens of millions),
so it cannot route through the general `RuleEvaluationEngine` (regex per event would dominate the decode).

Each format decoder consults the active scope in its decode loop **before** constructing the record:

- `ETLProcessor.DecodeWithTraceEvent` → `if (scope != null && !scope.Keep(e.ProviderName, e.TimeStamp.ToUniversalTime(), (int)level)) return;` before `new ETLLogLine(e)`.
- Text / tracefmt, EVTX, etc. → the same check mapped to each format's provider/time/level. (ETL first;
  others follow — see §11.)

## 8. Plumbing (how the scope reaches the decoder)

The scope comes from the search's loaded RuleDSL (`NuSearchQuery.LoadedRules` via `RulesConfigPaths`).
`NuSearchQuery` builds the `ScopeRule` from the `scope` sections and publishes it to the decoders for the
duration of the scan, then clears it.

- **Prototype:** a process-global `ETLProcessor.ActiveScope`, set at scan start and cleared at scan end.
  This is required because `FolderLocation` creates its **own** `ETLProcessor` per file during the scan, so
  a per-instance setting never reaches the decoder. (This exact mismatch silently no-op'd the first
  prototype — see the perf memory.)
- **Production:** thread the scope per-search through the location → processor factory so concurrent
  searches don't share global state. The static is a documented prototype shortcut, not the end state.

## 9. Cache interaction

A scoped load produces a **different result set** than a full load, so it must use a **distinct cache key**
(mirroring `EnrichmentCacheSuffix`): hash the scope (providers + time + level + mode) into the cache db
filename. Otherwise a scoped open could reuse — or poison — a full open's cache. A `scope=…` suffix on the
cache key keeps the two independent and both warm-reusable.

## 10. Triage-panel integration

The UI flow that produces a scope rule:
1. On opening a file above a size threshold, run a **bounded** `EtlInfoExtractor.Inspect` (providers,
   per-provider counts, time span) — seconds, not the full load.
2. Show the provider list (checkboxes + counts) and an optional time-range control.
3. On "Load selected", **generate a `scope` section** from the selection (`ScopeRule.ToSection(...)`),
   attach it to the search's rules, and run. The decode-time executor does the rest.

The panel never needs to know about decode internals — it just emits a scope rule. The scope can also be
saved as a normal `*.rules.json` for reuse ("my standard kernel-free view").

## 11. Limitations & future work

- **Per-format wiring.** ETL is first; the text/tracefmt, EVTX, CSV, etc. decoders each need the same
  pre-record check, with provider/time/level mapped to that format. Formats without a provider concept
  honor only the time/level parts.
- **The decode pass is the floor.** The file is still read/dispatched once to filter (TraceEvent dispatches
  every event); scope skips the wrap+insert, not the sequential read. That's why the 36 M scoped load is
  49 s, not ~5 s — ~13 s is the unavoidable decode-dispatch.
- **Predicate pushdown (future unification).** A natural extension: detect *regular* `filter` rules whose
  conditions only reference provider/time/level and auto-evaluate those at decode (as a scope), leaving the
  rest as a view filter. Then a `filter` on `providers` would transparently scope the load. `scope` is the
  explicit, validated version of that; pushdown is the implicit optimization.
- **WPP/tracefmt.** Provider-level scoping on the tracefmt text path is coarser (the text is already
  produced by tracefmt); time/level filtering of the parsed lines still applies.

## 12. Test plan

- **Unit (CoreTests/FindNeedleRuleDSLTests):** `ScopeRule.Keep` truth table (include/exclude providers,
  time window edges, levels); `ScopeRule.Validate` rejects a scope that tests `message`; `FromSections`
  AND-combines multiple scope sections.
- **Integration (ETWPluginTests, SkipCI):** `TriageScopeTests` — full vs scoped load on a real multi-provider
  capture; assert scoped ingests fewer rows and is faster (the measured 8.4×). Already prototyped.
- **Cache:** a scoped open and a full open of the same file keep independent caches (distinct keys) and both
  warm-reuse.

## 13. Files touched

- `FindNeedleRuleDSL/UnifiedRuleModel.cs` — add `timeFrom`/`timeTo`/`levels`/`providerMode` to
  `UnifiedRuleAction`.
- `FindNeedleRuleDSL/ScopeRule.cs` (new) — compiled scope + `FromSections` + `Validate` + `ToSection`.
- The rule loader / `GetSectionsByPurpose` — recognize `action.type == "scope"`.
- `ETWPlugin/FileExtension/ETLProcessor.cs` — decode-time `ActiveScope` check (replaces the prototype's
  raw `ProviderScope`).
- `FindPluginCore/Searching/NuSearchQuery.cs` — build the scope from loaded rules, publish/clear around the
  scan, and add the scope to the cache key.
- `FindNeedleUX/` (later) — the triage panel that emits a scope section.
- Tests as in §12.
```
