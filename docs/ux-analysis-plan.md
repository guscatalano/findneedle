# FindNeedle UX — Analysis + Action Plan (with prototypes)

> Augments [`ux-analysis.md`](./ux-analysis.md). Each finding keeps its evidence and gains a
> **→ Plan** (what I'd do, and where). Findings that are *substantial UX changes* (layout / flow /
> new affordances) include a **Prototype** — an ASCII before→after so the design can be reviewed
> before any code is written. Copy/label/attribute sweeps get a plan only (nothing to "see").

## Suggested do-order (phases)

1. **P0 — Correctness:** fix the unclickable Cancel (only functional defect); disable "Load selected" on an empty triage selection; add the zero-source guard. *(prototypes below)*
2. **P1 — First-run calm:** results first-paint (collapse filters + smart columns), the "No log loaded yet" empty state, Welcome banner trim. *(prototypes)*
3. **P2 — Honesty & trust:** remove/rewire the no-op "Deep" depth; level chips become the real filter; cache-reuse rewording; kernel-unchecked note. *(prototypes)*
4. **P3 — Consistency sweeps:** one noun ("Source"), product-name spelling, header style, emoji→Fluent, "Add Azure DevOps". *(copy/attribute, no prototype)*
5. **P4 — Accessibility:** AutomationProperties.Name on filter inputs + icon chrome; menu AccessKeys. *(attribute sweep)*
6. **P5 — Findability:** Diagnostics hub grouping; empty states for Locations/Rules/Logs; gate dev-only affordances. *(prototypes for the hub + empty states)*

---

# High

## Cancel button is unclickable during a search — no escape hatch
- **RunSearchPage — CancelButton** · User control & freedom
- **Why:** `SetControlsTo(false)` sets `this.IsEnabled = false` on the whole Page; a disabled WinUI ancestor disables all descendants, so `CancelButton.IsEnabled = true` never takes. Nav is also disabled. On a big load the user is trapped.
- **→ Plan (P0, small, high impact):** Stop disabling the Page. Wrap only the *inputs* (Run button, depth control, Browse, source list) in a named container (`InputsPanel`) and toggle `InputsPanel.IsEnabled`; leave `CancelButton` and the nav rail outside it and always enabled during a run. Add a FlaUI test that asserts Cancel is hit-testable mid-search. This also fixes the "can't leave the page" side of the bug.
- **Prototype:**
```
BEFORE  (whole page IsEnabled=false → Cancel dead)      AFTER
┌ Run search ─────────────────────────────┐            ┌ Run search ─────────────────────────────┐
│ [Source list]        (greyed)           │            │ ┌ InputsPanel (greyed while running) ─┐ │
│ Depth ◉◉◉            (greyed)            │            │ │ [Source list]  Depth ◉◉  [Browse]   │ │
│ [ Run ]              (greyed)            │            │ └──────────────────────────────────────┘ │
│ [ Cancel ]  ← greyed too (BUG)          │            │ [ Cancel ]  ← always enabled  ██▓░ 42% │
└──────────────────────────────────────────┘            └──────────────────────────────────────────┘
```

---

# Medium

## "Deep (thorough)" search depth is a no-op — identical to Normal  *(2 reviewers)*
- **RunSearchPage — Search Depth** · Match to real world / trust
- **Why:** Both Normal and Deep just set `_shallowSearch = false`. "Deep" changes nothing.
- **→ Plan (P2):** Collapse the three radios to a single two-state control that maps to the real `_shallowSearch` boolean, with plain labels and a consequence subtitle. Don't invent a third behavior we don't have. Drop the three identical 🔍 emoji.
- **Prototype:**
```
BEFORE                                   AFTER
Search depth                             Search depth
 🔍 ◉ Shallow                             ◉ Standard   — headers/first lines (fast)
 🔍 ○ Normal                              ○ Full       — every line, slower on big files
 🔍 ○ Deep (thorough)   ← does nothing
```

## One concept, four names: location / source / data source / connection  *(2+ reviewers)*
- **App-wide** · Match to real world / Consistency
- **Why:** Same "where data comes from" idea is called location/source/data source/connection across five screens.
- **→ Plan (P3, copy sweep):** Standardize on **"Source"** everywhere data is added/listed (Configure→"Sources", SearchLocationsPage title/subtitle, Welcome, viewer toolbar "Sources"). Keep **"Connection"** only for saved credentials, and always relate it ("a saved sign-in you attach to an online source"). Retire "location" and "data source". Do it as one grep-guided pass over the XAML `Text=`/`Title=`/subtitle strings + menu labels; no logic changes. *(No prototype — wording only.)*

## Running with no sources: page runs over nothing; status-bar buttons silently redirect
- **RunSearchPage + status-bar Run buttons** · Error prevention
- **Why:** Status-bar guards `Locations.Count==0` but silently navigates away; RunSearchPage has no guard and runs over nothing → "No results."
- **→ Plan (P0):** One shared helper `TryEnsureSourcesOrPrompt()` used by every run entry point: if zero sources, show an inline `InfoBar` (not a silent nav) with an **Add source** action, and keep focus. Disable RunSearchPage's Run button while `Locations.Count==0`; de-emphasize the status-bar "Run → View Results" (see Low finding) until a source exists.
- **Prototype:**
```
┌ Run search ──────────────────────────────────┐
│ ⓘ Add at least one source before running.     │
│    [ Add source ]                             │
│                                               │
│ [ Run ]  ← disabled until a source exists     │
└───────────────────────────────────────────────┘
```

## Long searches show only an indeterminate ring — no magnitude or time
- **QuickLogWithRulesPage — ProgressRing + StatusText** · Visibility of status
- **Why:** Fixed strings + 24px spinner while a blocking `RunSearch().Wait()` runs; looks hung on large files.
- **→ Plan (P1):** This page should consume the same streaming progress the MainWindow/RunSearch flow already has (rows scanned, current file, phase label from `FlowProgress`/`SearchProgressSink`). Replace the bare ring with a phase line + running row count + elapsed. Reuse the shared loader component proposed in "two run entry points".
- **Prototype:**
```
BEFORE                          AFTER
   ◐  Working…                  Decoding system.etl  (WPP)     ⏱ 0:38
                                Scanned 1,240,880 rows · phase 3/4
                                ██████████░░░░░  ~70%
```

## Run Search page shows no pre-run summary of what will be searched
- **RunSearchPage — "Search Summary"** · Recognition over recall
- **Why:** Summary box is empty until *after* a run.
- **→ Plan (P1):** Populate on page load + on any source/rule change from current pipeline state. Empty → the zero-source InfoBar above.
- **Prototype:**
```
Will search:  2 sources · app.log, system.etl
              0 rule files · depth: Standard
```

## First load dumps the entire UI: full toolbar + expanded dense filter panel  *(2 reviewers)*
- **NativeResultsPage — toolbar/FiltersPanel/grid** · Cognitive load
- **Why:** `FiltersToggle IsChecked="True"` expands the whole left panel (time chips + 5 field boxes + Known toggle + level legend + 2 clear buttons) on first paint, competing with results.
- **→ Plan (P1, substantial):** Default the filter panel **collapsed** for a fresh profile (persist last state after that). Inside the panel, split into "Common" (search, Level, time presets) shown by default and an "More filters ▾" disclosure for per-field boxes / Known / custom range. Pair with the active-filter badge (below) so collapsing is safe.
- **Prototype:**
```
BEFORE (first paint)                              AFTER (first paint)
┌Filters▾──────────────┬ results ┐               ┌▸│ Search: [__________]        results │
│⏱ 15m 1h 24h 7d …     │  ...    │               │F│ 10:02  Error   Failed to open…      │
│Provider [____] Task… │         │               │i│ 10:03  Warn    Retrying…            │
│Message  [__________] │         │               │l│ …                                    │
│Source [__] ☐Known▾   │         │               │t│                                      │
│Error Warn Info (leg) │         │               │▸│ (panel collapsed; ● badge if active) │
│[Clear][Clear all]    │         │               └─┴──────────────────────────────────────┘
└──────────────────────┴─────────┘               click ▸ → Common filters, then "More ▾"
```

## Results grid opens with ~19 mostly-ETW columns
- **NativeResultsPage — ResultsGrid** · Information density
- **Why:** ActivityId/ProviderGuid/Keywords/OpCode/RecordId/Raw Row etc. are empty for a plain text log → wide scroll of blanks buries Time/Message.
- **→ Plan (P1):** Pick the default visible column set from the loaded result's dominant source type (text → Time/Level/Message/Source/Provider; reveal ETW columns only when ETW/EVTX rows exist), or auto-hide columns that are 100% empty in the current set. Keep the "Columns ▾" flyout for opt-in. Persist user overrides per source type.
- **Prototype:**
```
BEFORE (text .log)                                  AFTER (text .log)
Time│Level│Prov│GUID│Activity│Related│Keywords│…    Time │ Level │ Message .............. │ Source
10:02  Err   —    —      —        —       —    …    10:02  Error   Failed to open port 8080  app.log
        ← 19 cols, ~14 empty, horizontal scroll        ← 4-5 relevant cols, no scroll
```

## Top-docked filter row doesn't wrap / no horizontal scroll — fields clip
- **NativeResultsPage — FilterRowPanel (top dock)** · Flexibility
- **Why:** Plain horizontal StackPanel in `TopFilterHost` with no ScrollViewer; right fields run off a narrow window.
- **→ Plan (P3, small):** Swap the top-dock host to `WrapPanelLite` (matching the time/level rows that already reflow), or wrap `TopFilterHost` in a horizontal `ScrollViewer`. Prefer wrap — no field becomes unreachable and it matches sibling rows. *(No prototype — behavioral.)*

## Kernel provider silently pre-unchecked → default "Load selected" drops kernel data
- **Triage "Choose what to load"** · Visibility / error prevention
- **Why:** `IsChecked = !isKernel` pre-unchecks Windows Kernel; header never says so.
- **→ Plan (P2):** Keep the smart default (kernel is the firehose) but make it *visible*: an ⓘ note on the kernel row + a one-line header explaining it. See the combined triage prototype below.

## Unchecking everything + "Load selected" silently loads the whole file
- **Triage "Choose what to load"** · Error prevention
- **Why:** Scope only staged when `0 < selected < total`; zero-checked falls through to a full load.
- **→ Plan (P0, small):** Disable "Load selected" when zero are checked (with hint "Select at least one provider"). Never treat empty-selection as "load everything." See triage prototype.
- **Prototype (covers this + kernel note + count header + select-all):**
```
┌ Choose what to load ───────────────────────────────────┐
│ Large log (512 MB) with 26 providers. Check the ones    │
│ you need — only those load (much faster).               │
│ ⓘ Windows Kernel is unchecked by default (very high     │
│    volume). Check it if you need kernel events.         │
│ [ Filter… ]                    [✓ Check all][✗ Uncheck] │
│ ┌────────────────────────────────── Events ──┐         │
│ │ ☐ Windows Kernel                 33,610,085 │ ⓘ      │
│ │ ☑ Microsoft-Windows-RPC              34,260 │        │
│ │ ☑ Microsoft-Windows-DotNETRuntime   359,559 │        │
│ │ …                                           │        │
│ └─────────────────────────────────────────────┘         │
│ counts are approximate (sampled)  ·  [Load selected]▐   │  ← disabled when 0 checked
│                                       [Load everything] │
└─────────────────────────────────────────────────────────┘
```

## Filter inputs lack accessible names; labels are visual-only  *(2 reviewers)*
- **NativeResultsPage — Provider/TaskName/Message/Source boxes + combos** · Accessibility
- **→ Plan (P4, attribute sweep):** Add `AutomationProperties.Name` (or `LabeledBy` → the adjacent label) to every filter TextBox/ComboBox/DropDownButton. Placeholder text is not a name. One XAML pass. *(No prototype.)*

## Icon-only chrome buttons have tooltips but no accessible name  *(2+ reviewers)*
- **MainWindow chrome + viewer dismiss button** · Accessibility
- **→ Plan (P4):** Add `AutomationProperties.Name` matching each tooltip (McpHelp, StatusEdit, StatusHide, output-files dismiss). Introduce a shared `IconButton` style that *requires* a name so this can't regress. *(No prototype.)*

## Diagnostics fragmented across unrelated menus
- **SystemInfo / Logs / Statistics / About / Preferences(Logs)** · Findability
- **→ Plan (P5):** Add a top-level **Diagnostics** menu gathering System Check, Logs, Search statistics, Export performance report, Send logs to dev. Leave thin cross-links from the old spots for one release.
- **Prototype:**
```
MenuBar:  Start | Sources | Rules | Run & Results | Inspect | Diagnostics | Settings
                                                              └ System check
                                                                Logs
                                                                Search statistics (why so slow?)
                                                                Export performance report
                                                                Send logs to dev…
```

## Pervasive undefined jargon (RuleDSL / Pipeline Sections / purposes)
- **SearchRulesPage** · Match to real world / Help
- **→ Plan (P5):** Lead the page with one plain sentence ("Rules automatically tag, filter, scope, and enrich your results — optional"), add an inline "Learn more" → `FindNeedleRuleDSL/README.md`, and add one-line tooltips to each purpose value. Pair with the Rules empty-state prototype below.

## Page headers use three sizes/weights + inconsistent emoji/caps
- **All config/result pages** · Consistency
- **→ Plan (P3):** Define one `PageTitleStyle` (single size+weight) + a small `PageHeader` user-control (title + optional subtitle), adopt sentence case (the majority), decide emoji **out** of titles. Migrate pages to it. *(No prototype — style resource.)*

## Main settings page labeled "viewer" but holds app-wide settings  *(2 reviewers)*
- **ResultsViewerSettingsPage** · Match to real world
- **→ Plan (P3):** Rename menu "Preferences…" and PaneTitle to **"Settings"**; reword the subtitle so it doesn't claim everything is viewer-scoped; keep "viewer" wording only on the Appearance/Columns sections. *(No prototype — wording.)*

---

# Low

## Level legend chips look clickable but are inert  *(2 reviewers)*
- **NativeResultsPage — LevelsRowPanel vs LevelFilterCombo** · False affordance
- **→ Plan (P2, substantial):** Make the chips the *real* level filter (toggle/multi-select on click, matching the Time-preset chips that already work), and retire the separate "Level (all)" combo. One control, obvious behavior. If keeping the combo is preferred, restyle the legend as flat non-interactive "Level counts" labels — but merging is better.
- **Prototype:**
```
BEFORE (two controls, only the combo works)      AFTER (chips ARE the filter)
 ▊Error 12  ▊Warn 3  ▊Info 40    Level:[all ▾]     [▊Error 12]✓  [▊Warn 3]  [▊Info 40]✓
      (inert legend)              (real filter)     click a chip to include/exclude that level
```

## No indication filters are active when the pane is collapsed
- **NativeResultsPage — Filters toggle** · Visibility
- **→ Plan (P1):** Badge the toggle with an accent dot + active count when any field/time/level/search filter is set. Ships with the collapse-by-default change so collapsing is safe.
- **Prototype:** `[ ▸ Filters ]  →  [ ▸ Filters ● 3 ]`

## "No results" empty state assumes a search ran; no first-run guidance  *(3 reviewers)*
- **NativeResultsPage — EmptyOverlay** · First-run / recovery
- **→ Plan (P1, substantial):** Add a third overlay variant to the tested `ResultsOverlayPolicy`: **NothingLoaded** (fresh/no data) distinct from **NoMatches** (search ran, 0 hits) and **NoRowsFiltered** (filters hide all). Each gets a primary action. Only say "returned no rows" when a search actually executed; add a "Fix decoding" link when a decode warning exists.
- **Prototype:**
```
NothingLoaded (fresh)                 NoMatches (search ran)            Filtered-to-empty
┌───────────────────────────┐         ┌───────────────────────────┐    ┌───────────────────────┐
│          🔍                │         │  No matching events        │    │ No rows match filters  │
│  No log loaded yet         │         │  The source had no events  │    │                        │
│  Open a log or add a       │         │  that matched / decoded.   │    │  [ Clear filters ]     │
│  source to start.          │         │  [ Open another log ]      │    │                        │
│ [Open log file][Add source]│         │  [ Fix decoding ] (if warn)│    └───────────────────────┘
└───────────────────────────┘         └───────────────────────────┘
```

## Online-source buttons dead-end on first run
- **SearchLocationsPage → Online sources** · Error prevention
- **→ Plan (P5):** When no connection of that kind exists, relabel the button **"Set up Kusto…"** and route straight into connection creation, then return to add the source. Detect via `ConnectionStore` count at render. *(No prototype — relabel + routing.)*

## "Configured Locations" list has no empty state
- **SearchLocationsPage** · Empty states
- **→ Plan (P5, small):** Add the same `EmptyHint`/`RecentEmpty` pattern already used by siblings ("No sources added yet — use New above to add a folder, file, or live Event Log"). Reuses an existing style.

## "Rule files" tab has no empty state or way to create a first rule
- **SearchRulesPage** · Onboarding
- **→ Plan (P5, substantial):** Add an empty-state panel: one-line explanation + **"Add a starter rule"** button that writes a commented template into the AI-rules sandbox and selects it + a docs link. Show placeholder rows in the empty lists.
- **Prototype:**
```
┌ Rule files ──────────────────────────────────────────┐
│ Rules auto-tag, filter, scope, and enrich results.    │
│ You don't need any to search.                         │
│  [ 📂 Browse… ]  [ ✨ Add a starter rule ]  Learn more │
│  ──────────────────────────────────────────────       │
│  (no rule files yet)                                  │
└───────────────────────────────────────────────────────┘
```

## Rules hub opens on the empty "Active" runtime tab
- **RulesPage → SearchProcessorsPage** · Onboarding
- **→ Plan (P5, small):** Default `_initialTag` to a config tab ("Rule files") when no search has run; reorder nav so "Active" follows the config tabs. Replace the empty "Active" InfoBar with a "run a search to see which rules fired" hint. *(No prototype — default + reorder.)*

## Two "add data" surfaces (Search Locations vs Log Finder)
- **SearchLocationsPage vs LogFinderPage** · Cognitive load
- **→ Plan (P3):** Clarify subtitles — Log Finder: "Quick-open a known log as a **new** search"; Sources: "Build up the sources for the **current** search" — and cross-link. (Full unification is a bigger project; defer.) *(No prototype — wording.)*

## Two disconnected search flows (quick-open vs build-a-pipeline)
- **WelcomePage vs Configure→Run** · Consistency
- **→ Plan (P1):** Make quick-open the primary framing (covered by the Welcome prototype); demote the pipeline to a "Building a custom pipeline?" expander. See Welcome prototype.

## First-screen intro banner is dense; navigates by prose
- **WelcomePage IntroBanner** · Minimalist
- **→ Plan (P1, substantial):** Trim to a single "Start here" line tied to the Quick-Action tiles; move the feature list + jargon behind "Learn more". Prototype:
```
BEFORE                                          AFTER
ℹ Getting started                               Find a needle in your logs.
  Configure ▸ Locations / Connections /         Start here →  open a log below.
  Rules… then run from Run & Results.
  Features: RuleDSL · ETW · enrichment ·        ┌ 📂 Open   ┐┌ 📁 Open  ┐┌ ⏱ Recent ┐
  diagrams · Kusto · …                          │   file   ││  folder  ││  searches │
  (two-column wall of text + jargon)            └──────────┘└──────────┘└──────────┘
                                                Building a custom pipeline? ▸ (expander)
```

## Prominent green "Run → View Results" is a dead-end for a fresh profile
- **MainWindow status strip (run_view)** · Visibility
- **→ Plan (P0):** Same fix as the Medium zero-source finding from the status-bar side: de-emphasize until a source exists + inline hint instead of a silent nav. Covered by `TryEnsureSourcesOrPrompt()`.

## Results search box front-loads DSL syntax
- **NativeResultsPage — SearchBox** · Match to real world
- **→ Plan (P1, small):** Placeholder → **"Search all columns…"**; the "?" flyout keeps the advanced syntax. Prototype: `[ Search all columns…                    ? ]`

## Query help uses abbreviated tokens that don't match column names
- **NativeResultsPage — "?" flyout** · Terminology
- **→ Plan (P3):** Reword the help to the visible names and note aliases: "message (msg), processid (pid), threadid (tid), eventid". *(No prototype — copy.)*

## Message clipped by fixed 26px rows with no expand affordance
- **NativeResultsPage — Message column** · Discoverability
- **→ Plan (P1, small):** Add a hover tooltip with the full Message + a one-time inline hint ("click a row for full details") the first time results load; optionally a subtle chevron on hover. Keep the compact row height (it's good for scanning).
- **Prototype:** `10:02  Error  Failed to open port 8080 because… ⌄   ← hover shows full text / click = details`

## General Help buried in "…" overflow
- **NativeResultsPage — OverflowButton** · Help
- **→ Plan (P3):** Promote Help to a top-level "?" split (query syntax + "Full docs"), and give the overflow button a clearer glyph/label. *(No prototype — promotion.)*

## Filter-performance button shows cryptic metric text in the toolbar
- **NativeResultsPage — FilterPerfButton** · Terminology
- **→ Plan (P3):** Move it into the overflow/Diagnostics area, or only surface it when a filter is measurably slow (>Xms), with a labeled icon. *(No prototype.)*

## Two near-identically named clear buttons
- **NativeResultsPage — "Clear filters" vs "Clear all filters"** · Consistency
- **→ Plan (P1, small):** Collapse to one **Clear ▾** with "Field filters only" / "Everything (incl. search & time)". Removes the guess.
- **Prototype:** `[ Clear ▾ ]  →  ▫ Field filters only   ▫ Everything (search, time, level)`

## "Known ▾" toggle label is opaque; caret implies a menu
- **NativeResultsPage — ShowKnownToggle** · Recognition
- **→ Plan (P2):** Rename to **"Pick from values"** and drop the misleading caret (it's a toggle, not a menu). Add a one-time hint. *(No prototype — relabel.)*

## No "Select all / Deselect all" in the provider list
- **Triage dialog** · Efficiency
- **→ Plan (P0):** "Check all"/"Uncheck all" scoped to the filtered rows. Shown in the triage prototype above.

## Per-provider count column unlabeled
- **Triage dialog** · Recognition
- **→ Plan (P0):** "Events" header + surface the "approximate (sampled)" note whenever counts came from a bounded scan. In the triage prototype above.

## "Inspect ETL" jumps to a file picker with no preamble
- **Inspect menu → Inspect ETL** · Consistency
- **→ Plan (P3, small):** Give Inspect ETL the same short intro dialog Inspect Binary now has ("Reads a summary — build, capture window, event/lost counts, providers — without a full load"). Reuses the existing pattern. *(No prototype — mirrors Inspect Binary.)*

## Cache-reuse prompt uses insider terms; silently defaults to reuse
- **CacheReusePromptService** · Match to real world / recovery
- **→ Plan (P2):** Reword to outcomes, and consider defaulting to Rescan for older caches; surface a small "reused previous scan" note when the prompt is skipped.
- **Prototype:**
```
BEFORE                                   AFTER
Use cached results?                      Open previous results?
[ Use cache ]  [ Rescan ]                Scanned 3 min ago · 1.2M rows
(insider terms; default reuse)           [ Open previous (fast) ]  [ Rescan now (up to date) ]
```

## Two "run a search" entry points, inconsistent design
- **RunSearchPage vs QuickLogWithRulesPage** · Consistency
- **→ Plan (P2, substantial):** Extract a shared `SearchProgress` loader control (phase + row count + elapsed) and shared status wording, and adopt QuickLog's cleaner stepwise+validation layout on RunSearchPage. Converges the two mental models. (Prototype = the QuickLog long-search loader above.)

## Auto-add "Edit condition" relies on magic strings, no validation
- **AutoAddRulesPage → Edit condition** · Error prevention
- **→ Plan (P3):** Replace source-kind free text with a multi-select of the known kinds; validate/normalize on Save (warn on unknown tokens); click-to-insert examples; optional "would this match your loaded logs?" preview.
- **Prototype:**
```
Source kinds:  [☑ ETW] [☑ EventLog] [☐ Folder] [☐ File] [☐ Zip]     (was: free-text "ETW,EventLog")
Path glob:     [ **\CBS\*.log            ]  e.g. **\Panther\*.log
⚠ "EWT" is not a known source kind        ← inline validation on Save
```

## Field-extraction toggle explained with an expert wall of text
- **ReformatRulesPage** · Cognitive load
- **→ Plan (P3):** One-line benefit ("Turn extracted fields into sortable/filterable columns") + a "Learn more ▾" expander for the perf/cache/MCP details. *(No prototype — copy restructure.)*

## Feature naming inconsistent (Field extraction / reformat / message-reformat) + per-row toggle unlabeled
- **ReformatRulesPage / RulesPage / AutoAddRulesPage** · Consistency + a11y
- **→ Plan (P3+P4):** Pick **"Field extraction"** across tab/page/dialog; bind `AutomationProperties.Name` on the per-row toggle to the rule name. *(No prototype.)*

## "Logs" page is a bare toggle + empty list, no title/description
- **LogsPage** · Empty state / help
- **→ Plan (P5, small):** Add a title + one-line description + an empty-state message ("No entries yet. Enable Debug Logging to capture detail."). Reuses empty-state styling.

## Two "Logs" concepts under Settings
- **Preferences(Logs) vs Settings→Logs** · Consistency
- **→ Plan (P5):** Rename the Preferences category to **"Support"** (it's just "send logs to dev" + settings path), and/or link it to the real Logs page. Resolved for free if the Diagnostics hub lands. *(No prototype.)*

## System Check flags missing tools but offers no fix
- **SystemInfoPage** · Recovery
- **→ Plan (P5):** Each failing check gets an action link — "Install…" → Diagram Tools, "Set TMF path…" → decoding settings — so the user acts from where the problem is shown.
- **Prototype:** `✗ Mermaid (Node) — not found     [ Install… ]`

## Same three online providers via two divergent UIs
- **SearchLocationsPage vs ConnectionsPage** · Consistency
- **→ Plan (P3):** One shared provider list/component (label + icon) rendered on both pages so they can't drift; always spell out "Azure DevOps". *(No prototype — shared component.)*

## "Add ADO" jargon abbreviation
- **SearchLocationsPage** · Match to real world
- **→ Plan (P3):** Button text → "Add Azure DevOps" (widen the fixed 120px). Folds into the shared-component fix. *(No prototype.)*

## Connections add-buttons rely on emoji, no accessible name
- **ConnectionsPage** · Accessibility
- **→ Plan (P4):** Mark emoji `AccessibilityView="Raw"` and set explicit `AutomationProperties.Name`. Folds into the shared-component fix. *(No prototype.)*

## Two competing icon systems (color emoji vs Fluent)
- **App-wide** · Consistency / a11y
- **→ Plan (P3):** Standardize action affordances on Fluent `SymbolIcon`/`FontIcon`; strip decorative emoji from buttons/radios/titles (or mark `AccessibilityView="Raw"` if intentional). Do it alongside the header-style + shared-component work. *(No prototype — sweep.)*

## Product name spelled two ways
- **Title bar / Explorer integration vs Welcome/About** · Consistency
- **→ Plan (P3, tiny):** Canonical **"FindNeedle"**; fix the window `Title` + Explorer-integration strings. *(No prototype.)*

## All three Search Depth options share the 🔍 icon
- **RunSearchPage** · Minimalist
- **→ Plan (P2):** Moot once depth collapses to two plain options (see the Deep finding). Drop the emoji. *(No prototype.)*

## Hidden status bar has no on-screen way back
- **MainWindow — StatusHideButton** · Recovery
- **→ Plan (P3):** Move "×" into the gear/overflow menu (not a one-click sibling of edit) or leave a thin persistent restore handle when hidden.
- **Prototype:** `hidden state:  ⎯⎯⎯⎯⎯ ▴ show status bar ⎯⎯⎯⎯⎯   ← thin restore handle`

## "MCP" toggle occupies prime top-right space with pure jargon
- **MainWindow top-right** · Minimalist / match to real world
- **→ Plan (P3):** Move the MCP toggle into Settings (or a status-bar chip); if it stays, label it "AI agent server (MCP)". Frees the header for the actual first task. *(No prototype — relocate.)*

## First menu "Quick" is an ambiguous label
- **MainWindow MenuBar** · Recognition
- **→ Plan (P3):** Rename to **"Start"** (or "Open") — it holds Welcome + Open File/Folder/with-Rules, the real entry points. Matches the Diagnostics-hub menu prototype above. *(No prototype.)*

## Menu bar / status icons lack mnemonics + names
- **MainWindow** · Accessibility
- **→ Plan (P4):** Add `AccessKey` to top-level menus and `AutomationProperties.Name` to gear/hide icon buttons. Folds into the a11y sweep. *(No prototype.)*

## Status strip shows "Last run: —" before any search
- **MainWindow status strip (lastrun)** · Minimalist
- **→ Plan (P1, tiny):** Return null for the Last-run segment until a search has produced results (mirrors how "filters"/"outputfiles" already suppress at zero). *(No prototype.)*

## "Preview first-run (new user)…" exposed to every user
- **MainWindow Settings menu (preview_new_user)** · Minimalist
- **→ Plan (P3):** Gate behind a debug/developer flag (env var or a hidden Diagnostics toggle) so it's not in the shipped Settings menu. *(This is the item we just added — reviewers are right that it's a dev tool.)* *(No prototype.)*

## Test hook ("Test file path" + "🚀 Load") exposed in production
- **SearchRulesPage** · Minimalist
- **→ Plan (P3):** Remove the box/Load from shipped UI (keep `AddRuleFileByPath` for automation) or hide behind the same debug flag. *(No prototype.)*

## Emoji-laden "Rule files" tab vs clean Fluent siblings
- **SearchRulesPage vs sibling rule tabs** · Consistency
- **→ Plan (P3):** Replace the emoji with the Fluent glyphs the sibling tabs use. Folds into the icon sweep. *(No prototype.)*

---

## What I'd ship first (my recommendation)

If you want a single high-leverage PR: **P0 + the P1 first-paint set.** That's the Cancel fix, the zero-source guard, the triage empty-selection guard — plus collapsing the filter panel, smart columns, the "No log loaded yet" empty state, and the Welcome trim. Those six changes remove the "overwhelming / looks broken / feels hung / can't escape" reactions a first-time user hits in the first two minutes, and none of them touch the search engine. The consistency/accessibility sweeps (P3/P4) are lower-risk and can land incrementally after.
