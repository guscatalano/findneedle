# FindNeedle — Prioritized UX Backlog

> Derived from the persona × use-case matrix in [`personas-and-use-cases.md`](./personas-and-use-cases.md),
> the findings in [`ux-analysis.md`](./ux-analysis.md) / [`ux-analysis-plan.md`](./ux-analysis-plan.md),
> and a code sanity-check of the flows. **Prioritization = impact × frequency, then effort.**
> - **Impact/frequency** comes from how many personas run the flow and how often (UC6 filter, UC1 open,
>   UC5 correlate are the widest; UC3 WPP and UC8 dump are deep and *defining* for P1 Priya).
> - **Effort**: S (hours), M (a day-ish), L (multi-day / structural).
>
> Legend for **Serves**: personas P1–P6 and use cases UC1–UC13 (see the personas doc).

---

## 0 · Already shipped (this UX pass)

State so the backlog reflects reality. All committed on `master`.

- **Correctness (P0):** unclickable Cancel fixed; zero-source guard; triage empty-selection guard
  (+ check/uncheck-all, Events header, kernel-unchecked note). — *UC1, UC2*
- **First-run calm:** "No log loaded yet" empty state (+ Open/Add actions); calmer search placeholder;
  suppressed empty "Last run"; trimmed Welcome banner; smart auto-hide of empty ETW columns. — *UC1, UC6*
- **Terminology & naming:** one noun "Source"; product name "FindNeedle" everywhere; no-op "Deep"
  depth removed. — *all*
- **Filtering:** level legend chips *are* the filter, now **multi-select** (Error + Warning); active-
  filter badge; disambiguated Clear buttons. — *UC6*
- **Findability / IA:** Diagnostics menu; Tools & Help menus; Settings → gear button; Configure now
  holds Sources/Rules/Connections + workspace ops; dev-only affordances gated. — *UC7, UC13, all*
- **Reliability niceties:** Recent items are checkboxes with added-state; empty states for Configured
  sources / Rule files / Logs; symbol-resolution log reachable on successful WPP decode. — *UC2, UC3, UC4*

---

## 1 · NOW (highest leverage still open)

### 1.1 Run feedback: numeric progress + pre-run summary  · **M**
- **What.** Show rows-scanned / current file / elapsed during a run (not a bare spinner), and populate
  the Run Search "summary" on load (sources · rules · depth) so the user sees what will run *and* that
  it isn't hung. Reuse one shared progress component across RunSearch + QuickLogWithRules.
- **Serves.** UC1, UC2 · P2 Marcus, P4 Sam, P5 Dana (anyone who runs a real search).
- **Why now.** "Feels hung" is the #1 mid-flow anxiety on large loads — the exact files these personas open.

### 1.2 Time-density strip in the viewer (click-to-zoom)  · **M**
- **What.** A compact histogram/density bar over the filtered set's time range, in the viewer — click a
  bucket to set the From/To window. The data already exists (`GetHistogramAsync`) but is **MCP-only**.
- **Serves.** UC5 · P2 Marcus, P5 Dana, P6 Raj.
- **Why now.** UC5 (correlate by time) is wide, and the analysis showed there's *no* human histogram —
  a high-value gap where the backend is already built.

### 1.3 RuleDSL onboarding: starter template + live match preview + config-first  · **M**
- **What.** Open the Rules hub on a *config* tab (not the empty "Active" runtime tab); lead with plain
  language ("Rules auto-tag, filter, scope, enrich — optional"); add a **"Add a starter rule"** that
  writes a commented template; add a live **"would this match your loaded log?"** preview.
- **Serves.** UC7 · P3 Elena, P2 Marcus.
- **Why now.** UC7 is the leverage flow for the automation persona; today it's expert-only.

### 1.4 Message discoverability: row-expand affordance + full-text tooltip  · **S**
- **What.** A hover tooltip with the full Message and a one-time "click a row for details" hint; keep
  the compact row height. Message is clipped at 26px with no cue that details exist.
- **Serves.** UC1, UC6 · P4 Sam + all.

### 1.5 Accessibility sweep  · **M**
- **What.** `AutomationProperties.Name` on every filter input + icon-only chrome button (a shared
  IconButton style that *requires* a name); `AccessKey` mnemonics on the top-level menus.
- **Serves.** all personas; the one finding category with zero coverage so far.

---

## 2 · NEXT

### 2.1 Filter wording: "Pick from values" + query-help token names  · **S**
- Rename "Known ▾" → "Pick from values" (drop the misleading caret); make the query-syntax help use the
  visible column names (message/processid/threadid) or note the aliases (msg/pid/tid). — *UC6 · all*

### 2.2 Cache-reuse prompt rewording  · **S**
- Reword to outcomes ("Open previous results (fast)" vs "Rescan now (up to date)"); surface a small
  "reused previous scan" note when auto-reused. — *UC12 · P2, P3, P5*

### 2.3 Online sources: stop the dead-end + consistent naming  · **M**
- When no connection exists, relabel the button "Set up Kusto…/ADO…/GitHub…" and route into connection
  creation, then return to add the source; always spell "Add Azure DevOps"; share one provider
  component between Sources and Connections. — *UC4, UC10 · P2, P3, P6*

### 2.4 Clarify the two "add data" surfaces  · **S**
- Distinct subtitles: Log Finder = "quick-open a known log as a **new** search"; Sources = "build the
  set for the **current** search"; cross-link. — *UC4 · P2, P6*

### 2.5 Auto-add condition validation + pickers  · **M**
- Replace magic-string source-kind boxes with a multi-select of known kinds; validate/normalize on save
  (warn on typos like "Eventlog"); click-to-insert examples. — *UC7 · P3, P2*

### 2.6 MCP discoverability  · **M**
- Relabel the top-right toggle "AI agent server (MCP)"; add a "what can an agent do?" surface (the tool
  catalog / a copyable connect snippet). — *UC10 · P3 Elena.*

### 2.7 System Check remediation links  · **S**
- Each failing check gets an action: "Install…" → Diagram Tools, "Set TMF path…" → decoding settings.
  — *UC13 · P1, P5, dev.*

### 2.8 Polish sweep  · **S each**
- Hidden status-bar restore handle; one shared page-title style + sentence case; emoji → Fluent icon
  vocabulary; connections emoji accessibility. — *all*

---

## 3 · LATER / bigger bets

### 3.1 Toolbar direction  · **M now / L later**
- Ship **D3** (primary "Open log"/"Run" CTA + labeled Settings/Help) now — cheapest fix for the buried
  gear and the first-run "what do I do". Prototype **D4** (left activity rail, Settings pinned at the
  foot) as the long-term structure — it minimizes friction across *all* flows in the click-path grid.
  — *all; see the toolbar mockups + flow grid.*

### 3.2 Command palette (Ctrl+Shift+P)  · **L**
- Searchable command surface for power users; complements (not replaces) the primary toolbar. Strong
  for repeat/keyboard use. — *UC6, UC10 · P1, P3, P5.*

### 3.3 Cross-source time normalization  · **L**
- Audit and fix time-zone / format alignment so events from different source kinds truly interleave on
  one timeline (correctness, not chrome). — *UC5 · P2, P5, P6.*

### 3.4 Sequence-diagram authoring in-flow  · **M**
- Templates for the UML rule + a "diagram this selection/activity" action from the viewer, so UC9
  doesn't require hand-writing a rules file. — *UC9 · P1, P3.*

### 3.5 Unify Sources + Log Finder  · **M**
- Fold the two "add data" surfaces into one consistent action (beyond the subtitle fix in 2.4). — *UC4.*

### 3.6 Graduate Inspect Binary from "experimental"  · **S (validation)**
- Validate the WEVT_TEMPLATE + TraceLogging extraction against a corpus of real binaries, then drop the
  experimental label. — *UC11 · P1, P5.*

---

## Summary table

| # | Item | Serves | Effort | Tier |
|---|------|--------|:------:|:----:|
| 1.1 | Run feedback: progress + pre-run summary | UC1/UC2 · P2/P4/P5 | M | NOW |
| 1.2 | Time-density strip (click-to-zoom) | UC5 · P2/P5/P6 | M | NOW |
| 1.3 | RuleDSL onboarding (template + preview) | UC7 · P3/P2 | M | NOW |
| 1.4 | Message expand affordance | UC1/UC6 · P4/all | S | NOW |
| 1.5 | Accessibility sweep | all | M | NOW |
| 2.1 | "Pick from values" + query tokens | UC6 · all | S | NEXT |
| 2.2 | Cache-reuse rewording | UC12 | S | NEXT |
| 2.3 | Online-source dead-end + naming | UC4/UC10 | M | NEXT |
| 2.4 | Clarify Sources vs Log Finder | UC4 | S | NEXT |
| 2.5 | Auto-add validation/pickers | UC7 | M | NEXT |
| 2.6 | MCP discoverability | UC10 · P3 | M | NEXT |
| 2.7 | System Check remediation links | UC13 | S | NEXT |
| 2.8 | Polish sweep (title style, icons, restore handle) | all | S | NEXT |
| 3.1 | Toolbar: ship D3, prototype D4 | all | M/L | LATER |
| 3.2 | Command palette | UC6/UC10 · P1/P3/P5 | L | LATER |
| 3.3 | Cross-source time normalization | UC5 | L | LATER |
| 3.4 | In-flow diagram authoring | UC9 · P1/P3 | M | LATER |
| 3.5 | Unify Sources + Log Finder | UC4 | M | LATER |
| 3.6 | Graduate Inspect Binary | UC11 | S | LATER |

**Recommended next sprint:** 1.1 → 1.2 → 1.4 (highest impact on the widest flows UC1/UC5/UC6), then 1.5
(accessibility) and 1.3 (unlocks the automation persona). 3.1's D3 can ride along cheaply.
