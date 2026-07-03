# FindNeedle — User Personas & Use Cases

> **v1.2 (this revision).** A deliberate *sharpening* pass, not more detail. Changes from v1.1, and the
> reasoning, are in the [Changelog](#changelog) at the bottom. The short version: **6 personas → 4**
> (the thin/overlapping ones were folded, not deleted — see the [mapping](#persona-mapping-v11--v12)),
> plus four things v1.1 lacked — **non-goals / anti-personas**, a **frequency-weighted matrix** so
> priority is *derived* rather than asserted, **measurable success** per use case wired to the app's
> own instrumentation (`UxMonitor` + `PerfLog`), and a **validation plan** (what signal would confirm or
> kill each persona).
>
> **Honesty note (unchanged and important):** these personas are still *builder-authored composites*,
> not real users. v1.1 code-verified the *flows*; it did **not** verify the *people*. Nothing here is
> validated demand — treat it as a falsifiable hypothesis. The whole point of the new validation section
> is to stop this doc from silently ratifying the roadmap it came from.

## What FindNeedle is (one paragraph)

FindNeedle is a Windows log-search and analysis desktop app (WinUI 3) plus a CLI, built on a
plugin + declarative-rule (RuleDSL) pipeline. It ingests many source kinds — folders, single files,
ZIP/CAB archives, live and saved Windows Event Logs, ETW `.etl` traces (manifest **and** WPP, decoded
via the WDK's `tracefmt` with symbol resolution), packet captures, kernel crash dumps (ETW extracted
via the debugger), and online sources (Kusto, Azure DevOps, GitHub) — normalizes them into a common
event row, and lets the user search, filter, correlate, enrich, tag, visualize (PlantUML/Mermaid), and
export. It scales from tiny text logs (in-memory) to multi-million-row captures (SQLite + FTS5 trigram
index, with decode-time provider/time **scoping** to avoid loading the whole thing). An in-app MCP
server lets an AI agent drive the same capabilities headlessly.

---

## Who it is *not* for (non-goals & anti-personas)

Personas earn their keep by letting us say **no**. If a request serves only the people below, it's out
of scope by default — and that's a feature decision, not an oversight.

- **Not a live dashboard / SIEM / alerting tool.** FindNeedle analyzes logs you *point it at*; it does
  not stream, watch, alert, or maintain always-on dashboards. **Anti-persona: "Dana-the-dashboard-watcher"**
  who wants live tiles, thresholds, and paging. Every "real-time tiles" request should be declined or
  routed to a SIEM.
- **Not a log-collection / shipping agent.** It does not deploy to endpoints, tail files continuously,
  or forward to a store. Ingestion is on-demand, local, analyst-driven.
- **Not an APM / metrics / tracing-vendor product.** No spans-as-a-service, no metric rollups, no hosted
  backend. Diagrams are derived from matched log rows, not from an instrumentation SDK.
- **Not cross-platform.** Windows-only by design (WinUI 3, ETW, Event Log, WDK). A Linux/syslog analyst
  is explicitly not a target; don't contort the model to fit them.
- **Not a general text editor.** It won't be Notepad++/VS Code for editing; the value is search/filter/
  correlate/enrich at scale, not authoring.
- **Not a hosted/multi-user service.** Single-user desktop + local CLI + local MCP. No accounts, no
  sharing server, no RBAC.

If a proposed feature only makes sense for one of these, it's a signal we're drifting.

---

## Personas (4)

We keep a persona only if it drives a **distinct default-UX decision**. Four do; the other two from
v1.1 were folded into them (their unique needs preserved — see mapping). IDs P1–P4 are stable so the
[backlog](./ux-backlog.md) keeps resolving.

### Persona summary

| # | Persona | Drives which default-UX decision | Tech level | Core job | Frequency |
|---|---------|----------------------------------|-----------|----------|-----------|
| **P1** | **Priya Nair — the Trace Expert** *(incl. large-capture "scale mode", was P5 Dana)* | **Detail-dense & transparent**: never hide provider/symbol/activity detail; scope *before* loading at volume | Expert (ETW/WPP/kernel, scale) | Root-cause a defect from a trace — or mine a firehose capture down to the slice that matters | Daily |
| **P2** | **Marcus Bell — the Support Triager** *(incl. forensic variant, was P6 Raj)* | **Recipe-reuse & one-search-across-formats**: bundle in, find the error, save the recipe | High (Windows internals/forensics, not kernel) | Triage a customer/incident bundle, find the failure, attach evidence | Many times/day |
| **P3** | **Elena Rossi — the Automator** | **Machine-drivable & reproducible**: everything the UI does must be scriptable (RuleDSL + MCP) | High (automation, scripting) | Encode analysis as versioned rules; run headless per failure; let an agent do the first pass | Daily, mostly headless |
| **P4** | **Sam Okafor — the Casual Dev** | **Zero-setup quick path**: open → red line → done, no pipeline, no jargon | Medium (app logs, some ETW) | Open today's log, jump to the error, read context, move on | A few times/week |

> **The productive tension to keep visible:** P1 pulls the UI toward *density and control*; P4 pulls it
> toward *calm and zero-setup*. Most first-run friction is this tug-of-war. The resolution the app uses
> — progressive disclosure (calm default, expert affordances one click away) — is a direct consequence
> of holding both personas rather than averaging them away.

### P1 — Priya Nair · the Trace Expert *(absorbs P5 Dana's scale mode)*

- **Context.** Storage/networking driver work. Repro runs produce `.etl` (WPP from the driver + kernel +
  manifest providers); symbols in a private PDB share + public MS symbol server; often debugging off the
  capture machine. **Scale mode (the former Dana):** also analyzes multi-GB / multi-hour captures
  (36M+ events, 20+ providers) where the first problem is *volume*, not decode.
- **Technical level.** Expert. ETW, WPP, TMF/PDB, provider GUIDs, ActivityIds, WinDbg, `tracefmt`,
  `xperf`. Impatient with tooling that hides detail or "just works" opaquely.
- **Two jobs, one persona (kept together deliberately):**
  1. *RCA a single trace* — the one sequence of events that explains a hang/bugcheck/corruption: which
     provider, which thread/activity, what ordering, what was missing.
  2. *Scope a firehose* — see what's *in* the capture (providers + counts + span) and load only the
     slice worth analyzing, before waiting minutes.
- **Why one persona:** both are "an expert who will not be dumbed down and needs the truth about the
  data." They pull the UI the same way (density, transparency, scoping). Where they diverge — *follow
  one activity* vs *facet millions* — is a **mode**, noted per use case, not a separate person.
- **Frustrations.** WPP decode is finicky (TMF/PDB paths, `_NT_SYMBOL_PATH`, symsrv next to dbghelp);
  huge traces take minutes and blow memory; she needs 2–3 of 26 providers; correlating ActivityId across
  providers is manual; "why didn't this decode?" is a black box; she can't see per-provider volume before
  committing to a load.
- **Success looks like.** Open the trace, load *only* the providers/time window she cares about within
  seconds, see decoded WPP with a clear record of which symbols resolved and which didn't, and follow one
  activity end-to-end (ideally as a sequence diagram).
- **Quote.** *"Don't decode 40 million kernel events I'll never read. Show me my provider, tell me exactly
  which TMF you couldn't find, and let me follow the ActivityId."*
- **Primary use cases.** UC2, UC3, UC8, UC9, UC11, UC13; heavy on UC5/UC6.

### P2 — Marcus Bell · the Support Triager *(absorbs P6 Raj's forensic variant)*

- **Context.** Receives customer log bundles (ZIP/CAB with `CBS.log`, `DISM.log`, Panther `setupact.log`,
  Windows Update/Defender logs, exported `.evtx`). Under SLA; dozens of cases a day; rarely has the
  machine. **Forensic variant (the former Raj):** sometimes the "bundle" is incident artifacts —
  exported Security/System logs, a packet capture, occasionally a memory dump — and the deliverable is a
  defensible, time-ordered evidence view.
- **Technical level.** High on Windows servicing/setup/eventing/forensics; not a kernel or ETW-internals
  person.
- **What he's trying to accomplish.** Find the failing operation and its root error across a pile of
  differently-formatted logs, then attach the evidence (filtered view / export), and make the *next*
  identical case one click via a saved recipe.
- **Why the forensic variant folds here (not its own persona):** the flow is the same — *many mixed
  artifacts in, one time-ordered search, export the evidence*. Its only genuinely unique needs are packet
  captures and dump-ETW extraction, which live as **edge use cases** (UC8 + packet ingest), not a whole
  persona. Keeping a thin "Raj" was diluting priority without changing a single UI decision.
- **Frustrations.** Every log is a different format; he greps by hand; timestamps differ in zone/format;
  he re-does the same triage per case with no reusable recipe; he can't see the `HRESULT` and the
  operation that produced it together.
- **Success looks like.** Drop the whole bundle in, one search finds the error across every file, filter
  to the failure window, export; a saved rule/workspace makes the next case one click.
- **Quote.** *"I have 20 cases in the queue. I don't want to learn ETW — I want to open the bundle, find
  the HRESULT, and see what happened right before it."*
- **Primary use cases.** UC1, UC4, UC5, UC6, UC7, UC12; UC8 (forensic).

### P3 — Elena Rossi · the Automator

- **Context.** Owns the failure-analysis step of a CI pipeline. Thousands of runs; each failure drops
  logs (text + ETW + custom EventSource/TraceLogging). Wants analysis *automated, consistent, surfaced
  back to engineers*.
- **Technical level.** High. Scripts everything; comfortable with JSON rule files, the CLI, and driving
  tools programmatically (the MCP server is aimed squarely at her).
- **What she's trying to accomplish.** Encode "what a human analyst looks for" as reusable RuleDSL
  (filter/enrichment/tag) + field extraction, run it headlessly per failure, and hand engineers a
  pre-triaged, tagged, columnized result — or let an agent do the first pass.
- **Frustrations.** Analysis logic lives in heads; results aren't reproducible; a new signature means
  editing scattered scripts; no clean machine-drivable interface.
- **Success looks like.** A versioned set of `.rules.json`, run over each failure, that tags/extracts the
  known signals; an MCP agent that opens a run, applies rules, summarizes; stable columns.
- **Quote.** *"If a person can recognize this failure, I can write a rule for it — and never triage it by
  hand again. Give me something an agent and a pipeline can both drive."*
- **Primary use cases.** UC7, UC10, UC6, UC12; UC1/UC5 secondary.

### P4 — Sam Okafor · the Casual Dev

- **Context.** Builds a Windows desktop/service app that writes plain-text logs and some EventSource/
  TraceLogging. Debugs his own feature; occasionally a coworker's repro.
- **Technical level.** Medium. Knows his app's logs cold; ETW is fuzzy; doesn't want to become a trace
  expert to read a log.
- **What he's trying to accomplish.** Open today's log, jump to the error, read the few lines around it,
  move on. Occasionally confirm his EventSource is firing.
- **Frustrations.** Notepad/VS Code choke on big logs and can't filter by level/time; he doesn't know
  which ETW columns matter; 19 columns + a wall of filters is intimidating.
- **Success looks like.** Double-click / "Open log" → results appear → type "error" or click the Error
  chip → read context → done, no setup.
- **Quote.** *"I just want to open the file and find the red line. I don't want a pipeline."*
- **Primary use cases.** UC1, UC6; UC11 secondary.
- **Why P4 is non-negotiable despite low frequency:** he's the *counterweight* that keeps the expert
  personas from turning the default experience into a cockpit. Cut him and the calm-default rationale
  collapses.

### Persona mapping (v1.1 → v1.2)

| v1.1 | v1.2 | What happened to its unique needs |
|------|------|-----------------------------------|
| P1 Priya (Trace Expert) | **P1** | Kept; broadened to include scale mode. |
| P2 Marcus (Support) | **P2** | Kept; broadened to include forensic variant. |
| P3 Elena (Automator) | **P3** | Kept unchanged. |
| P4 Sam (Casual dev) | **P4** | Kept unchanged. |
| **P5 Dana (Perf/scale)** | folded → **P1** "scale mode" | Inspect-before-load, per-provider volume, decode-time scoping → P1's second job. Nothing lost; it drives the *same* density/transparency decisions. |
| **P6 Raj (IR/security)** | folded → **P2** "forensic variant" | Cross-artifact timeline + evidence export → P2's flow. Packets + dump-ETW extraction survive as **edge use cases** (UC8 + packet ingest), not a persona. |

---

## Use cases

Use-case IDs are **stable** from v1.1 (the backlog references them). Each lists the job, personas,
trigger, today's flow, and — new in v1.2 — a **measurable success** target tied to real instrumentation:
`UxMonitor` (records any interaction > 2500 ms to `ux-slow.log` with row-count/storage/filter conditions)
and `PerfLog` (phase events: `search.run`, `viewer.*.load`, `consolidate.skipped`, `enrich.rule`,
`ux.slow`, …). Targets are **hypotheses to calibrate**, not SLAs — but they're at least falsifiable.

### UC1 — Open a single log and find the failure *(the "quick path")*
- **Personas.** P4 (primary); P2, P3 (secondary).
- **Trigger.** Double-click a `.log`/`.txt`/`.etl`/`.evtx`, drag-drop, or Quick → Open log.
- **Flow.** Open → (large/streaming loads show progress) → viewer → type a term / click the **Error**
  chip → read the row + neighbors (details pane) → export/copy.
- **Measurable success.** For a ~100k-row text log: first rows visible **< 3 s**, open→first-filter with
  **zero `ux.slow` records** in the path; `PerfLog search.run` + first `viewer.*.load` under ~10 s p50.
- **Friction today.** First paint can be busy for a plain text log (mitigated: empty ETW columns
  auto-hide; filters default to a left rail; calmer placeholder; "No log loaded yet" first-action guide).

### UC2 — Triage & scope a large capture before loading it
- **Personas.** P1 scale-mode (primary); P2 (secondary).
- **Trigger.** Opening a large `.etl` (≥ threshold) auto-offers triage; or Tools → Inspect ETL.
- **Flow.** Open → **"Choose what to load"** scans the whole file, lists every provider + counts
  (searchable/sortable) → user checks the providers they need (high-volume Windows Kernel starts
  unchecked, with a note) → a **scope rule** filters at *decode time* → viewer opens on the scoped subset.
- **Measurable success.** Provider list **complete** (no under-count vs `Inspect ETL`); scoped load wall
  time and peak memory a **fraction** of the full load (compare `PerfLog search.run` scoped vs full);
  the choice is reversible.
- **Friction today.** Fixed: kernel-heavy under-count; empty-selection no longer silently loads all;
  incomplete-scan surfaced with a "Scan the whole file" escape.

### UC3 — Decode a WPP / ETW driver trace with symbols
- **Personas.** P1 (primary).
- **Trigger.** Opening a WPP `.etl`; symbol/TMF configured (MS symbol server on by default).
- **Flow.** Open → fast pre-scan estimates decodability (fails fast with the missing TMF GUIDs) →
  `tracefmt` (assembled from the installed WDK + Debuggers closure) decodes → viewer → **Search
  statistics** exposes the symbol-resolution log (what it tried / worked / didn't).
- **Measurable success.** Decoded WPP rows present; the resolution log is reachable on a **successful**
  decode (not only on failure); **no shipped redistributables** (always the installed WDK).
- **Note.** This is *low-frequency but high-differentiation* — see [the two axes](#two-axes-frequency-vs-differentiation).

### UC4 — Investigate a customer log bundle across sources
- **Personas.** P2 (primary).
- **Trigger.** A ZIP/CAB (or folder) with CBS/DISM/Panther/Event Logs/etc.
- **Flow.** Add the archive/folder (archives auto-expand; `.cab` via `expand.exe`) → run → one normalized
  set → global search / per-field filters find the error across every file → filter to the failing
  operation → export.
- **Measurable success.** One search spans every file/format; `HRESULT`/error + its operation visible
  together; export succeeds. No `ux.slow` on the cross-file search for a typical bundle.
- **Friction today.** Two "add data" surfaces (Sources vs Log Finder) still need clearer role labels
  (subtitles done; deeper unify is [backlog 3.5](./ux-backlog.md)).

### UC5 — Correlate events across sources by time
- **Personas.** P1, P2 (primary); P3 (secondary).
- **Trigger.** Multiple sources loaded (logs + event logs + capture).
- **Flow.** Load → run → sort by **Time** → set From/To (or a relative preset) → read the interleaved,
  time-ordered events → narrow with the **time-density strip** (click-to-zoom, level-colored) + chips.
- **Measurable success.** A single correctly time-ordered view across heterogeneous sources; windowing
  via the strip applies **< 2.5 s** (no `ux.slow`). **Open correctness risk:** cross-source time-zone/
  format normalization is not proven — flagged, not claimed.
- **Status.** The viewer histogram gap called out in v1.1 is **closed** (the density strip shipped).

### UC6 — Filter, facet, and drill into the results
- **Personas.** All four (the flow everyone spends the most time in).
- **Trigger.** Any loaded result set.
- **Flow.** Global substring or **structured query** (`msg ~ error AND level == Warning`); per-field
  filters; **known-value** pick-lists (multi-select, cross-filtered); **Level** chips (multi-select);
  time window; column show/hide; row tagging; details pane; export.
- **Measurable success.** On multi-million-row sets: substring/filter apply **p95 < 2.5 s** (FTS trigram;
  watch `UxMonitor` `filter.apply` scopes), pick-lists populate without an O(rows) scan, **no empty
  combos**, active-filter badge always reflects state.
- **Friction today.** Historically the biggest source of small bugs (empty combos, inert chips, overlay
  flicker) — most fixed; column defaults now source-aware; row text top-aligned + single-line.

### UC7 — Encode analysis as a RuleDSL rule (tag / enrich / extract fields)
- **Personas.** P3 (primary); P2 (secondary).
- **Trigger.** A recurring failure signature or a field worth promoting to a column.
- **Flow.** Configure → Rules → author/import a `*.rules.json` (or MCP `save_rule`) → apply → rules run
  in-scan (extraction stays on the streaming path) → matched rows tagged / extracted fields become
  sortable-filterable columns → save the workspace.
- **Measurable success.** Reproducible/versioned; extracted fields behave as first-class columns; the
  **Active rules** page shows real per-rule match counts + timing (`enrich.rule` in `PerfLog`) so a slow
  rule is visible; a **live "would this match?" preview** exists in the editor (shipped).
- **Friction today.** Still jargon-heavy for newcomers (config-first Rules hub + starter rule + plain
  framing done; deeper templating remains).

### UC8 — Recover ETW logs from a kernel crash dump
- **Personas.** P1 (primary); P2 forensic (secondary).
- **Trigger.** A kernel/complete `.dmp` (no `.etl` saved).
- **Flow.** Add the `.dmp` → `cdb` + `!wmitrace.strdump`/`logsave` extract each logger's buffers to
  `.etl` (per-logger isolation) → normal ETW decode → viewer.
- **Measurable success.** ETW recovered when no trace file exists; robust to a single bad logger; a clear
  diagnostic when a debugger/OS-build mismatch blocks extraction.
- **Note.** *Low-frequency, high-differentiation* (see below). Niche but defining for P1.

### UC9 — Visualize an interaction as a sequence diagram
- **Personas.** P1, P3.
- **Trigger.** A trace whose messages describe an interaction (RPC, request→handler→response, a state
  machine).
- **Flow.** Author a UML rule (`participants` + `rules`) → apply → **generate outputs** → Mermaid/PlantUML
  `.mmd`/`.puml` (+ image if the render tool is installed) → view/export. Drivable via MCP.
- **Measurable success.** A readable sequence diagram derived from the *actual* matched rows; deferred
  outputs preview before generation.
- **Note.** *High-differentiation, lower-frequency.*

### UC10 — Drive analysis with an AI agent (MCP)
- **Personas.** P3 (primary); any power user.
- **Trigger.** Enable the in-app MCP server (top-right toggle); an agent connects.
- **Flow.** Agent: add sources / run → set rules → filter/page (`set_filter`, `get_page`, `facets`,
  `histogram`, `templates`) → tag, generate diagrams, read the app's own diagnostics.
- **Measurable success.** A repeatable scriptable first-pass; agent and human share one state.
- **Friction today.** Discoverability of "what an agent can do" and the "MCP" jargon are gaps (toggle
  relabeled; a tool-catalog/connect-snippet surface remains).

### UC11 — Inspect without loading  *(was "supporting"; promoted to first-class)*
- **Personas.** P1 (primary); P4 (secondary, "is my EventSource even firing?").
- **Flow.** Tools → **Inspect ETL** (build/capture window/providers/counts) or **Inspect Binary** (the
  ETW providers a native EXE/DLL declares — WEVT_TEMPLATE manifest + TraceLogging metadata).
- **Measurable success.** Accurate provider list without a full load; Inspect Binary graduates from
  *experimental* only after corpus validation ([backlog 3.6](./ux-backlog.md)).

### UC12 — Reuse a prior result / workspace  *(was "supporting"; the weighting says it's top-tier)*
- **Personas.** All four — everyone reopens work.
- **Flow.** Reopen a cached search (warm SQLite cache) or a saved workspace (sources + rules + filters).
  The window title now shows the active workspace + source/rule counts.
- **Measurable success.** A warm-cache reopen skips the scan (`LastSearchReusedCache` true; no
  `search.run` scan cost); the reuse-vs-rescan choice is clear (reworded prompt).
- **Why promoted:** the [frequency-weighted matrix](#weighted-persona--use-case-matrix) scores it as high
  as UC1. Demoting it to "supporting" in v1.1 was a mistake this pass corrects.

### UC13 — Diagnose "why is this slow?"
- **Personas.** P1 (primary); the developer.
- **Flow.** Diagnostics → Search statistics / perf log; System check (cdb/WDK, symbol path, diagram
  tools) with remediation links.
- **Measurable success.** The report attributes wall time to phases; System check offers a fix action per
  failing check; `ux-slow.log` captures any > 2.5 s interaction with conditions.

---

## Weighted persona × use-case matrix

Priority should be **derived**, not asserted. Each persona gets a rough **weight** (reach × frequency);
each use case scores **Σ (persona weight × involvement)**, involvement = primary 2 / secondary 1 / none 0.

> ⚠️ **The weights are hypotheses**, not measurements — we have no telemetry yet (see
> [validation](#validation-plan--what-would-confirm-or-kill-a-persona)). They're written down precisely so
> they can be *argued with and later replaced by real numbers*.

**Persona weights (hypothesis):** P1 = 30 · P2 = 30 · P3 = 20 · P4 = 20.
Rationale: P1/P2 are daily/many-times-daily and are the tool's core audience; P3 is daily but headless
and fewer people; P4 is many people but infrequent + shallow.

| Use case | P1 (30) | P2 (30) | P3 (20) | P4 (20) | **Score** |
|----------|:--:|:--:|:--:|:--:|:--:|
| UC6 Filter / facet / drill | ● | ● | ● | ● | **200** |
| UC1 Open & find failure | ○ | ● | ○ | ● | **150** |
| UC12 Reuse result / workspace | ○ | ● | ● | ○ | **150** |
| UC5 Correlate by time | ● | ● | ○ | | **140** |
| UC7 RuleDSL tag/enrich | | ● | ● | | **100** |
| UC9 Sequence diagram | ● | | ● | | **100** |
| UC2 Triage & scope large | ● | ○ | | | **90** |
| UC8 ETW from crash dump | ● | ○ | | | **90** |
| UC13 Why so slow | ● | ○ | | | **90** |
| UC11 Inspect without loading | ● | | | ○ | **80** |
| UC10 AI agent (MCP) | | ○ | ● | | **70** |
| UC3 WPP/ETW decode + symbols | ● | | | | **60** |
| UC4 Bundle across sources | | ● | | | **60** |

● primary (2) · ○ secondary (1)

**What the derivation surfaces (that the v1.1 assertion hid):**
- **UC12 (reuse) ties UC1 for #2.** It was buried as "supporting." Corrected: it's promoted and given
  measurable success.
- **UC6 dominates** — every persona, primary. It should get the most polish/perf budget, and it has.
- **UC3 and UC4 rank *last* by frequency** (single-primary flows) despite being defining. That's the
  point of the next section.

### Two axes: frequency vs differentiation

Frequency-weighted score answers "what do we polish for the common path." It is **not** the only axis.
Some low-score use cases are *why someone chooses FindNeedle over `grep`/Notepad at all*:

| | High frequency | Low frequency |
|---|---|---|
| **High differentiation** | UC6 filter-at-scale, UC5 correlate | **UC3 WPP+symbols, UC8 dump-ETW, UC9 diagram, UC2 scope** |
| **Low differentiation** | UC1 open, UC12 reuse | UC4 bundle, UC10 MCP (today) |

- **High-freq × high-diff (UC6, UC5):** the crown jewels — invest relentlessly.
- **Low-freq × high-diff (UC3, UC8, UC9, UC2):** don't optimize for volume, but they must be *excellent
  and trustworthy* — they're the demo that wins P1/P2. A bug here costs more than its frequency implies.
- **High-freq × low-diff (UC1, UC12):** table stakes — must be frictionless, but they won't differentiate.
- Nothing should live in low-freq × low-diff for long; if it does, question whether it earns its surface.

---

## Validation plan — what would confirm or kill a persona

The single biggest weakness of this doc is that it's **unvalidated**. There is no telemetry today. Below
is how we'd get honest signal *without* betraying the "not a hosted service / no accounts" non-goal —
i.e. **local, opt-in, privacy-preserving counters** (aggregate event tallies written next to the perf
log, never content, never network), plus qualitative checks.

| Persona | Confirming signal | Killing / "we're wrong" signal |
|---------|-------------------|-------------------------------|
| **P1 Trace Expert** | Meaningful share of sessions open `.etl`, run WPP decode, or use Inspect ETL / scoping (countable from `PerfLog` phase events) | Almost all sessions are text/evtx only; scoping + WPP paths rarely exercised → the "expert" is aspirational |
| **P2 Triager** | Frequent ZIP/CAB/folder-bundle opens; workspaces saved & reopened (UC12) | Bundles rare; nobody saves recipes → we built for a workflow no one runs |
| **P3 Automator** | MCP server enabled; rules authored/imported; CLI runs | MCP toggled ~never; rule files stay empty → automation is a story, not a user |
| **P4 Casual Dev** | Large share of sessions = single small `.log`/`.txt`, no rules, quick filter, close | If this never happens, the "calm default" investment is mis-aimed (but keep P4 as a design counterweight regardless) |
| **Anti-persona check** | Packet/dump opens near-zero → confirms folding Raj was right; no one asks for live dashboards → confirms the non-goal | Steady packet/dump/forensic demand → *reinstate a forensic persona*; repeated "live alerting" asks → revisit the SIEM non-goal |

**Concrete next step to make this real (small, respects non-goals):** add an opt-in local
`usage-tally.json` (counts only: source kinds opened, features invoked, MCP on/off, rules applied,
workspaces reused) alongside `perf-log.txt`, surfaced on the Diagnostics page and via MCP. One release of
that turns every weight and persona above from assertion into data — and this doc's v1.3 should be driven
by it, not by the author.

---

## How to use this doc

1. **Prioritize by the weighted matrix** for the common path (UC6 → UC1/UC12 → UC5), **and** protect the
   low-frequency/high-differentiation flows (UC3/UC8/UC9/UC2) as quality-critical.
2. **Design each flow end-to-end for its persona and mode** — UC2 fast+reversible for P1 scale-mode; UC1
   zero-setup for P4; UC12 one-click for P2.
3. **Instrument success** — wire the measurable targets to `UxMonitor`/`PerfLog`, and stand up the opt-in
   usage tally so v1.3 is data-driven.
4. **Hold the non-goals** — decline work that only serves an anti-persona.

---

## Changelog

### v1.2 (this revision) — sharpening pass
- **Personas 6 → 4.** Folded P5 Dana into **P1** ("scale mode") and P6 Raj into **P2** ("forensic
  variant"); documented the mapping and where each unique need went (nothing deleted). Kept a persona
  only if it drives a *distinct default-UX decision*; made that criterion explicit. Stable IDs P1–P4 so
  the backlog still resolves.
- **Added non-goals & anti-personas** — the section v1.1 lacked; states who the tool refuses to serve
  (SIEM/dashboard, collection agent, APM, cross-platform, editor, hosted service).
- **Weighted, derived matrix** — replaced the binary ●/○ prioritization with persona weights + a computed
  score. This *changed conclusions*: **UC12 (reuse) promoted from "supporting" to top-tier**, and UC11
  promoted to a first-class use case. Added the **frequency-vs-differentiation** framing so UC3/UC8/UC9
  aren't dismissed for low frequency.
- **Measurable success per use case** — tied to the app's real instrumentation (`UxMonitor` 2.5 s
  threshold + conditions; `PerfLog` phases incl. `enrich.rule`, `ux.slow`), replacing "feels fast."
- **Validation plan** — what local, opt-in signal would confirm or kill each persona and each anti-persona,
  with a concrete `usage-tally.json` proposal that respects the no-telemetry / no-hosted-service non-goals.
- **Status refresh** — UC5's "histogram is MCP-only" gap is now closed (density strip shipped); UC7 notes
  the live match preview + per-rule stats now exist.

### v1.1 — code-verification pass (prior)
- Sanity-checked use-case *flows* against the code. Confirmed drag-drop, ZIP/CAB auto-expand, triage/
  scope, WPP decode + symbol log, structured query + multi-select filters, dump ETW extraction, UML
  rules, MCP. **Corrected UC5**: the time histogram was MCP-only; no viewer chart existed at the time.
