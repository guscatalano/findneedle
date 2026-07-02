# FindNeedle — User Personas & Top 10 Use Cases

> Working draft (v1), authored from the current behavior of the app. The intent is to anchor UX
> decisions in *who* uses FindNeedle and *what job they are hiring it to do*, so flows can be
> designed and prioritized against real goals rather than screens. Treat the personas as composites,
> not real people; refine them with real user input as it arrives.

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

## Personas

### Persona summary

| # | Persona | Role | Tech level | Core job | Frequency |
|---|---------|------|-----------|----------|-----------|
| P1 | **Priya Nair** | Senior Windows driver/kernel engineer | Expert (ETW/WPP/kernel) | Root-cause driver/OS defects from ETW traces | Daily |
| P2 | **Marcus Bell** | Escalation / support engineer | High (Windows internals, not kernel) | Triage customer log bundles, find the failure | Many times/day |
| P3 | **Elena Rossi** | SDET / test-infra engineer | High (automation, scripting) | Turn CI/test-failure logs into repeatable, automated analysis | Daily, mostly headless |
| P4 | **Sam Okafor** | Application developer | Medium (app logs, some ETW) | Quickly find the error in my own app's logs | A few times/week |
| P5 | **Dana Kim** | Performance engineer | Expert (WPA/perf/large captures) | Scope and mine multi-GB captures without drowning | Weekly, high stakes |
| P6 | **Raj Patel** *(secondary)* | Incident-response / security analyst | High (forensics) | Build a timeline across event logs, packets, and dumps | Ad hoc, urgent |

---

### P1 — Priya Nair · Senior Windows Driver Engineer

- **Context.** Works on a storage/networking driver. Repro runs produce `.etl` traces (WPP from the
  driver + kernel + manifest providers). Symbols live in a private PDB build share; some in the public
  MS symbol server. Often debugging on a machine that is *not* the capture machine.
- **Technical level.** Expert. Fluent in ETW, WPP, TMF/PDB, provider GUIDs, activity IDs, WinDbg,
  `tracefmt`, `xperf`. Impatient with tooling that hides detail or "just works" opaquely.
- **What she's trying to accomplish.** Turn a raw trace into the *one* sequence of events that explains
  a hang/bugcheck/corruption: which provider, which thread/activity, what ordering, what was missing.
- **Frustrations with the status quo.** WPP decode is finicky (TMF/PDB paths, `_NT_SYMBOL_PATH`,
  symsrv next to dbghelp); huge traces take minutes to load; she only needs 2–3 providers out of 26;
  correlating by ActivityId across providers is manual; "why didn't this decode?" is a black box.
- **Success looks like.** Open the trace, load *only* the providers/time window she cares about within
  seconds, see decoded WPP messages with a clear record of which symbols resolved and which didn't, and
  follow one activity end-to-end (ideally as a sequence diagram).
- **Quote.** *"Don't decode 40 million kernel events I'll never read. Show me my provider, tell me
  exactly which TMF you couldn't find, and let me follow the ActivityId."*
- **Primary use cases.** UC2, UC3, UC8, UC9, UC6.

### P2 — Marcus Bell · Escalation / Support Engineer

- **Context.** Receives customer log bundles (a ZIP/CAB with `CBS.log`, `DISM.log`, Panther
  `setupact.log`, Windows Update logs, Defender logs, exported `.evtx`). Under SLA pressure; handles
  dozens of cases a day; rarely has the customer's machine.
- **Technical level.** High on Windows servicing/setup/eventing; not a kernel or ETW-internals person.
- **What he's trying to accomplish.** Find the failing operation and its root error across a pile of
  differently-formatted logs, then attach the evidence (a filtered view / export) to the case.
- **Frustrations.** Every log is a different format; he greps files by hand; timestamps are in different
  zones/formats; he re-does the same triage per case with no reusable "recipe"; he can't easily see the
  error `HRESULT` and the operation that produced it together.
- **Success looks like.** Drop the whole bundle in, one search finds the error across every file,
  filter to the failure window, and a saved rule/workspace makes the *next* identical case one click.
- **Quote.** *"I have 20 cases in the queue. I don't want to learn ETW — I want to open the bundle,
  find the HRESULT, and see what happened right before it."*
- **Primary use cases.** UC1, UC4, UC5, UC6, UC7.

### P3 — Elena Rossi · SDET / Test-Infra Engineer

- **Context.** Owns the failure-analysis step of a CI pipeline. Thousands of test runs; each failure
  drops logs (text + ETW + custom EventSource/TraceLogging). Wants the analysis *automated and
  consistent*, surfaced back to engineers.
- **Technical level.** High. Scripts everything; comfortable with JSON rule files, the CLI, and driving
  tools programmatically (the MCP server is squarely aimed at her).
- **What she's trying to accomplish.** Encode "what a human analyst would look for" as reusable RuleDSL
  (filter/enrichment/tag) + field extraction, run it headlessly per failure, and hand engineers a
  pre-triaged, tagged, columnized result — or let an agent do the first pass.
- **Frustrations.** Analysis logic lives in people's heads; results aren't reproducible; onboarding a
  new failure signature means editing scattered scripts; no clean machine-drivable interface.
- **Success looks like.** A versioned set of `.rules.json`, run over each failure, that tags/extracts
  the known signals; an MCP agent that can open a run, apply rules, and summarize; stable columns.
- **Quote.** *"If a person can recognize this failure, I can write a rule for it — and never triage it
  by hand again. Give me something an agent and a pipeline can both drive."*
- **Primary use cases.** UC7, UC10, UC6, UC1, UC5.

### P4 — Sam Okafor · Application Developer

- **Context.** Builds a Windows desktop/service app that writes plain-text logs and some
  EventSource/TraceLogging events. Debugs his own feature; occasionally looks at a coworker's repro.
- **Technical level.** Medium. Knows his app's logs cold; ETW is fuzzy; doesn't want to become a trace
  expert to read a log.
- **What he's trying to accomplish.** Open today's log, jump to the error, read the few lines around it,
  and move on. Occasionally confirm his EventSource is even firing.
- **Frustrations.** Notepad/VS Code choke on big logs and can't filter by level/time; he doesn't know
  which ETW columns matter; anything that opens with 19 columns and a wall of filters is intimidating.
- **Success looks like.** Double-click / "Open log" → results appear → type "error" or click the Error
  chip → read context → done, with no setup.
- **Quote.** *"I just want to open the file and find the red line. I don't want a pipeline."*
- **Primary use cases.** UC1, UC6, UC11 (secondary).

### P5 — Dana Kim · Performance Engineer

- **Context.** Analyzes multi-GB, multi-hour captures (36M+ events, 20+ providers). Cares about volume,
  time windows, and which provider is the firehose. Uses WPA-class tooling; FindNeedle for
  search/filter/triage that WPA is clumsy at.
- **Technical level.** Expert on captures, providers, and scale.
- **What she's trying to accomplish.** Inspect what's *in* a capture (providers, counts, time span,
  machine), then load only the slice worth analyzing, and search/facet within it fast.
- **Frustrations.** Full loads take minutes and blow memory; she wastes time loading providers she'll
  discard; she can't quickly see per-provider volume before committing.
- **Success looks like.** Inspect → see the 26 providers with counts → scope to the 2 she needs (a
  decode-time filter) → the load is a fraction of the file → sub-second substring search within it.
- **Quote.** *"Tell me what's in the file and how big each provider is *before* I wait five minutes to
  load all of it."*
- **Primary use cases.** UC2, UC5, UC6, UC11, UC3.

### P6 — Raj Patel · Incident-Response / Security Analyst *(secondary)*

- **Context.** Investigates an incident from mixed artifacts: exported Security/System event logs, a
  packet capture, sometimes a memory dump. Needs a defensible timeline.
- **Technical level.** High (forensics), not necessarily ETW-internals.
- **Accomplishing.** Correlate "what happened when" across artifacts into one time-ordered view;
  pull ETW buffers out of a dump when live capture wasn't running.
- **Success looks like.** All artifacts in one view, filtered to the incident window, exported as
  evidence; ETW recovered from the `.dmp`.
- **Primary use cases.** UC5, UC8, UC4, UC6.

---

## Top 10 use cases

Each use case lists the **job-to-be-done**, the **primary/secondary personas**, the **trigger**, the
**flow through the app today**, **what success is**, and **current friction** (linking to the findings
in `ux-analysis.md` where relevant).

### UC1 — Open a single log and find the failure *(the "quick path")*
- **Personas.** P4 Sam (primary); P2 Marcus, P3 Elena (secondary).
- **Job.** "Open this file/folder and get me to the error with zero setup."
- **Trigger.** Double-click a `.log`/`.txt`/`.etl`/`.evtx`, drag-drop, or Quick → Open log.
- **Flow.** Open file → (large/streaming loads show progress) → results viewer → type a term or click
  the **Error** level chip / set a level filter → read the row + its neighbors (details pane) → export
  or copy if needed.
- **Success.** Under ~10s from open to first relevant row for a normal log; no configuration required.
- **Friction today.** First paint can be busy (filter pane + many columns) for a plain text log
  (mitigated: empty ETW columns now auto-hide; filters default to a left rail). Placeholder no longer
  pushes DSL syntax at newcomers. The "No log loaded yet" state now guides the very first action.

### UC2 — Triage & scope a large capture before loading it
- **Personas.** P5 Dana (primary); P1 Priya, P2 Marcus (secondary).
- **Job.** "Don't make me load 36M events to look at two providers."
- **Trigger.** Opening a large `.etl` (≥ the triage threshold) auto-offers triage; or Tools → Inspect ETL.
- **Flow.** Open large `.etl` → **"Choose what to load"** dialog scans the whole file and lists every
  provider with event counts (searchable, sortable) → user checks the providers they need (the
  high-volume Windows Kernel starts unchecked, with a note) → FindNeedle writes a **scope rule** that
  filters at *decode time*, so only those providers/time-window are ingested → viewer opens on the
  scoped subset.
- **Success.** Provider list is complete and accurate; scoped load is a fraction of the full file's
  time/memory; the choice is reversible ("Load everything" / re-scan).
- **Friction today.** Fixed: the picker under-counted providers on kernel-heavy files; empty-selection
  no longer silently loads everything; incomplete-scan is surfaced with a "Scan the whole file" escape.

### UC3 — Decode a WPP / ETW driver trace with symbols
- **Personas.** P1 Priya (primary); P5 Dana (secondary).
- **Job.** "Decode my driver's WPP messages, and tell me exactly what symbols did and didn't resolve."
- **Trigger.** Opening a WPP `.etl`; symbol/TMF path configured (MS symbol server on by default).
- **Flow.** Open `.etl` → fast pre-scan estimates decodability (fails fast with the missing TMF GUIDs
  if symbols are absent) → `tracefmt` (assembled from the installed WDK + Debuggers dbghelp/symsrv
  closure) decodes → viewer shows decoded messages → **Search statistics** page exposes the
  symbol-resolution log ("what it tried / what worked / what didn't", WinDbg `!sym noisy`-style detail).
- **Success.** Decoded WPP rows; a transparent, inspectable record of symbol resolution; no shipped
  redistributable binaries (always uses the installed WDK).
- **Friction today.** Symbol setup is inherently fiddly; the resolution log is now reachable from the
  Stats page on a *successful* decode (previously only appeared when symbols were missing).

### UC4 — Investigate a customer log bundle across sources
- **Personas.** P2 Marcus (primary); P6 Raj (secondary).
- **Job.** "Search one query across a whole bundle of differently-formatted logs."
- **Trigger.** A ZIP/CAB (or folder) containing CBS/DISM/Panther/Event Logs/etc.
- **Flow.** Add the archive/folder as a source (archives auto-expand; `.cab` via `expand.exe`) → run →
  the viewer holds all files' events in one normalized set → global search / per-field filters find the
  error across every file → filter to the failing operation → export the evidence.
- **Success.** One search spans every file/format; the `HRESULT`/error and its surrounding operation are
  visible together; result is exportable for the case.
- **Friction today.** "Add data" has two surfaces (Sources vs Log Finder) that need clearer role
  labels; terminology now standardized on "Source".

### UC5 — Correlate events across sources by time
- **Personas.** P2 Marcus, P5 Dana, P6 Raj.
- **Job.** "Put everything on one timeline and zoom to the incident window."
- **Trigger.** Multiple sources loaded (logs + event logs + capture).
- **Flow.** Load sources → run → sort by Time → set a From/To window (or a relative preset) → optionally
  a histogram/time view → read the interleaved, time-ordered events → narrow with level/provider chips.
- **Success.** A single, correctly time-ordered view across heterogeneous sources; fast windowing.
- **Friction today.** Time-zone/format normalization across formats is the hard part; the viewer's time
  presets/custom range exist; cross-source time normalization is a known area to validate.

### UC6 — Filter, facet, and drill into the results
- **Personas.** All (the flow everyone spends the most time in).
- **Job.** "Slice the loaded set down to the rows that matter."
- **Trigger.** Any loaded result set.
- **Flow.** In the viewer: global substring or **structured query** (`msg ~ error AND level == Warning`);
  per-field filters (Provider/TaskName/Message/Source); **known-value** pick-lists (multi-select,
  cross-filtered) instead of typing; **Level** chips (multi-select — Error + Warning together); time
  window; column show/hide; row tagging (Important/Question/…); details pane; export.
- **Success.** Fast narrowing on multi-million-row sets (SQLite FTS trigram for substring); filters are
  discoverable and their state is visible (active-filter badge); the level chips *are* the filter.
- **Friction today.** Historically the biggest source of small UX bugs (empty combos on large sets,
  inert level chips, overlay flicker) — most now fixed; column defaults now source-aware.

### UC7 — Encode analysis as a RuleDSL rule (tag / enrich / extract fields)
- **Personas.** P3 Elena (primary); P2 Marcus (secondary).
- **Job.** "Turn 'what I always look for' into a reusable rule that tags/extracts automatically."
- **Trigger.** A recurring failure signature or a field worth promoting to a column.
- **Flow.** Configure → Rules → author/import a `*.rules.json` (filter / enrichment / scope / uml /
  output sections; or author via MCP `save_rule`) → apply to the search → rules run in-scan (extraction
  enrichment stays on the streaming path) → matched rows are tagged / extracted fields become
  sortable-filterable columns → save the workspace so it's one click next time.
- **Success.** Reproducible, versioned analysis; new signatures are a rule edit, not a code change;
  extracted fields behave like first-class columns.
- **Friction today.** RuleDSL is powerful but jargon-heavy for newcomers (Rules page now has an empty
  hint + plainer framing to do); a live "would this match?" preview and starter templates are gaps.

### UC8 — Recover ETW logs from a kernel crash dump
- **Personas.** P1 Priya (primary); P6 Raj (secondary).
- **Job.** "Live capture wasn't running — pull the ETW buffers out of the `.dmp`."
- **Trigger.** A kernel/complete `.dmp` (no `.etl` was saved).
- **Flow.** Add the `.dmp` as a source → FindNeedle runs `cdb` + `!wmitrace.strdump`/`logsave` to extract
  each logger's buffers to `.etl` (logger names parsed; per-logger isolation so one bad logger doesn't
  abort) → those `.etl`s flow through the normal ETW decode path → viewer.
- **Success.** ETW events recovered from a dump when no trace file exists; robust to individual logger
  failures; clear diagnostic when the debugger/OS-version mismatch prevents extraction.
- **Friction today.** Requires a WinDbg/cdb at least as new as the capture OS build (surfaced as a
  decode warning); niche but high-value.

### UC9 — Visualize an interaction as a sequence diagram
- **Personas.** P1 Priya, P3 Elena.
- **Job.** "Show the hand-off/sequence between components as a picture, not 10,000 rows."
- **Trigger.** A trace whose messages describe an interaction (RPC, request→handler→response, a state
  machine).
- **Flow.** Author a UML rule (`participants` + `rules` that match log messages → sequence elements) →
  apply → **generate outputs** produces a Mermaid/PlantUML `.mmd`/`.puml` and (if the render tool is
  installed via Tools → Diagram Tools) an image → view/export. Drivable over MCP (`generate_outputs`).
- **Success.** A readable sequence/interaction diagram derived from the *actual* matched rows.
- **Friction today.** The DSL is interaction/sequence-oriented (not for frequency/volume charts — those
  are the viewer's histogram/facets); authoring needs the schema docs.

### UC10 — Drive analysis with an AI agent (MCP)
- **Personas.** P3 Elena (primary); any power user.
- **Job.** "Let an agent open the run, apply rules, filter, and summarize — headless or interactive."
- **Trigger.** Enable the in-app MCP server (top-right toggle); an agent connects.
- **Flow.** Agent: add sources / run search → set rules (`set_rules`, `save_rule`) → filter and page the
  viewer (`set_filter`, `get_page`, `facets`, `histogram`, `templates`) → tag rows, generate diagrams,
  read the app's own diagnostics — the same capabilities the UI exposes, machine-drivable.
- **Success.** A repeatable, scriptable first-pass triage; the agent and a human see the same state.
- **Friction today.** MCP is powerful but discoverability of "what an agent can do" and the toggle's
  jargon ("MCP") are UX gaps; interactively-authenticated MCP servers may be absent in headless runs.

---

### Secondary / supporting use cases (not in the top 10, but real)

- **UC11 — Inspect without loading.** Tools → Inspect ETL (build/capture window/providers/counts) or
  Inspect Binary (the ETW providers a native EXE/DLL declares, from its WEVT_TEMPLATE manifest +
  TraceLogging metadata). P1/P5. *Experimental for binaries.*
- **UC12 — Reuse a prior result / workspace.** Reopen a cached search (warm SQLite cache) or a saved
  workspace (sources + rules + filters). All personas.
- **UC13 — Diagnose "why is this slow?".** Diagnostics → Search statistics / perf log; System check for
  tool health (cdb/WDK, symbol path, diagram tools). P1/P5, and the developer.

---

## Persona × use-case matrix

| Use case | P1 Priya | P2 Marcus | P3 Elena | P4 Sam | P5 Dana | P6 Raj |
|----------|:--:|:--:|:--:|:--:|:--:|:--:|
| UC1 Open & find failure | ○ | ● | ○ | ● | ○ | ○ |
| UC2 Triage & scope large | ● | ○ | | | ● | |
| UC3 WPP/ETW decode + symbols | ● | | | | ○ | |
| UC4 Bundle across sources | | ● | ○ | | | ● |
| UC5 Correlate by time | ○ | ● | ○ | | ● | ● |
| UC6 Filter / facet / drill | ● | ● | ● | ● | ● | ● |
| UC7 RuleDSL tag/enrich | | ● | ● | | | ○ |
| UC8 ETW from crash dump | ● | | | | | ● |
| UC9 Sequence diagram | ● | | ● | | | |
| UC10 AI agent (MCP) | | ○ | ● | | ○ | |

● primary · ○ secondary

## How to use this doc

1. **Prioritize UX work** by the flows highest-value personas run most (UC1, UC2, UC6 are the widest;
   UC3/UC8 are deep and defining for P1).
2. **Design each flow end-to-end for its persona**, not screen-by-screen — e.g., UC2 should feel fast
   and reversible for Dana, and UC1 should feel zero-setup for Sam.
3. **Validate the personas** with real users; update the frequencies, frustrations, and the matrix.
