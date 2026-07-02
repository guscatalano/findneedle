# FindNeedle UX Review

> Generated 2026-06-30 via a multi-agent heuristic evaluation: 10 reviewers (one per UX
> surface + cross-cutting lenses) → adversarial verification of each finding against the code
> → synthesis. 67 findings survived verification (89 agents total). Read-only analysis.

## 1. Executive summary

For a brand-new user, FindNeedle currently reads as a powerful engine wrapped in an inconsistent, expert-facing shell. The core happy path works, but the first-run experience is undermined by three recurring themes: **terminology fragmentation** (the same "where data comes from" concept is called location / source / data source / connection across five screens, and the product name itself is spelled two ways on first launch), **missing or misleading empty/first-run states** (Configured Locations, Rule files, Logs, and the results grid all present blank or "search returned no rows" surfaces with no next step), and **false or dead-end affordances** (a prominent green "Run → View Results" that silently redirects, a "Deep (thorough)" search depth that does nothing, clickable-looking level chips that are inert, and online-source buttons that can never add anything on a fresh profile). The single most serious issue is functional, not cosmetic: **the Cancel button is unclickable during a search**, leaving no escape hatch on long runs. Accessibility is a consistent weak spot — icon-only chrome and per-column filter inputs rely on tooltips/placeholders that screen readers do not treat as names, and the menu bar has no keyboard mnemonics. Finally, two divergent "add data" and "run a search" flows, plus scattered diagnostics and a settings page mislabeled as "viewer-only," mean a newcomer cannot build a reliable mental model of how the app is meant to be used. The good news: most fixes are low-effort copy, default-state, and labeling changes, and the app already contains the right patterns (RecentEmpty, EmptyHint, per-button AutomationProperties.Name) that simply aren't applied consistently.

## 2. First-run journey

**Launch.** The window titles itself "Find Needle" while the Welcome hero says "FindNeedle" — two spellings in the first glance. The Welcome page leads with a dense two-column banner mixing getting-started prose ("Configure ▸ Locations / Connections / Rules… then run it from Run & Results") with a jargon-laden feature list (RuleDSL, ETW, enrichment) before the user has done anything. Meanwhile the top-right chrome is dominated by an undecodable "MCP" toggle, and the leftmost menu is labeled "Quick" — an interaction speed, not a content category — so the primary Open File/Open Folder actions are hidden behind an unpredictable label.

**Add a source.** The user faces two parallel surfaces (Search Locations vs Log Finder) with no explanation of which to use or that they behave differently (one accumulates sources, the other starts a fresh search-and-run). On the main Search Locations page, the "Configured Locations" list is completely blank with no empty state, so the primary page looks broken — despite the fact that sibling surfaces on the very same feature (Recent, Connections) do show helpful empty text. If the newcomer tries the prominent "☁ Add Kusto / Add ADO / Add GitHub" buttons, each dead-ends: with no saved connection they only pop a dialog sending the user elsewhere, never adding a source.

**Run.** The biggest friction point. The most prominent action in the shell — the accent-green "Run → View Results" — silently teleports a fresh (zero-location) profile to the Locations page with no message. The dedicated Run Search page does the opposite: it has no zero-source guard, runs over nothing, and dumps the user on "No results." The page offers three depth radios where "Deep (thorough)" is byte-for-byte identical to "Normal," and its "Search Summary" box is empty until after a run, so the user cannot confirm what will be searched before committing. On a real long-running load (via QuickLogWithRulesPage), feedback is a single static line and an indeterminate spinner — no row count, file, or elapsed time — so the app looks hung. And if a search does start, there is no working Cancel.

**Results.** First paint is a wall: a full toolbar, a fully-expanded left filter panel (time presets, five per-column inputs, a level legend, two similarly-named clear buttons), and a ~19-column grid full of empty ETW columns for a plain text log. The Message content is clipped at 26px rows with no expand hint. The level legend chips look exactly like the interactive time-preset chips but do nothing — the real level filter is a separate combo.

**Filter.** A newcomer clicks a level chip expecting to filter and nothing happens. Setting a filter and collapsing the pane hides data with no active-filter indicator. On a narrow window with "Filters on top," the right-most fields clip off-screen with no scroll. If the first search legitimately returns nothing, the empty state says "This search returned no rows" with no forward action — a dead end.

## 3. Prioritized findings

### Critical
_None identified._

### High

**Cancel button is unclickable during a search — no escape hatch**
- **Screen:** RunSearchPage — CancelButton
- **Heuristic:** User control & freedom (cancel/escape)
- **Why it hurts:** `SetControlsTo(false)` sets `this.IsEnabled = false` on the whole Page and then `CancelButton.IsEnabled = true`, but in WinUI a disabled ancestor disables all descendants regardless of their own state. The nav rail is also disabled (`DisableNavBar()`). Once a search starts there is no working way to cancel or leave until it finishes — the exact scenario `CancelButton_Click`/`_orchestrator.Cancel()` exists for is unreachable. On a large .etl/.zip this can trap the user for minutes.
- **Recommendation:** Never disable the whole Page. Disable only the specific input controls (Run button, depth radios, Browse) and keep the Cancel button in an enabled subtree. Verify Cancel is hit-testable during a long run.

### Medium

**"Deep (thorough)" search depth is a no-op — identical to Normal** _(flagged by 2 reviewers)_
- **Screen:** RunSearchPage — Search Depth radio group
- **Heuristic:** Match to the real world / error prevention / trust
- **Why it hurts:** Both `NormalSearch_Click` and `DeepSearch_Click` just set `_shallowSearch = false`, so "Deep" changes nothing. A user picking "Deep" to be thorough gets identical output with no indication, eroding trust. No tooltip explains what any level actually changes.
- **Recommendation:** Either wire Deep to real behavior (pass a real depth enum through the orchestrator) or remove it and present a single meaningful Shallow-vs-Full toggle. Add subtext describing the concrete tradeoff. Never ship a control choice with no effect.

**One concept, four names: location / source / data source / connection** _(flagged by 2+ reviewers)_
- **Screen:** App-wide — Configure menu ("Locations"), SearchLocationsPage (title "Search Locations", subtitle "data sources"), WelcomePage ("Data sources"), NativeResultsPage toolbar ("Sources," glossed as "loaded locations"), ConnectionsPage ("Connections"), LogFinderPage ("Add app / location")
- **Heuristic:** Match to the real world / Consistency & standards
- **Why it hurts:** A newcomer cannot tell whether they add logs via a location, a source, a connection, or the log finder. The words fragment the mental model on nearly every screen touched during onboarding.
- **Recommendation:** Pick one primary noun (recommend **"Source"**) and apply it to menu, page titles, subtitles, buttons, dialog titles, and the viewer button. Reserve "Connection" strictly for reusable credentials and always relate it back ("a saved sign-in you attach to an online source"). Drop "location" and "data source" as synonyms.

**Running with no sources: dedicated page runs over nothing; status-bar buttons silently redirect**
- **Screen:** RunSearchPage `Button_Click` + status-bar `RunAndViewResults`/`RunSearchOnly`
- **Heuristic:** Error prevention / visibility of system status
- **Why it hurts:** Two run entry points behave inconsistently for the zero-location case a fresh profile always starts in. The status-bar paths guard `Locations.Count == 0` but then silently navigate to SearchLocationsPage with no message (the prominent green "Run → View Results" just teleports away). RunSearchPage has no guard at all — the accent Run button runs over nothing and lands on "No results." Same intent, two poor outcomes.
- **Recommendation:** Add one consistent zero-source check. Show an inline InfoBar/toast ("Add at least one source before running") with an "Add source" action instead of silently navigating or running empty. Disable/annotate RunSearchPage's Run button when `Locations.Count == 0`, and de-emphasize the status-bar Run button until a location exists.

**Long searches show only an indeterminate ring with no magnitude or time**
- **Screen:** QuickLogWithRulesPage — ProgressRing + StatusText
- **Heuristic:** Visibility of system status
- **Why it hurts:** `GoButton_Click` shows fixed strings and a 24px spinner while awaiting a blocking `RunSearch(...).Wait()`. No row count, file, percent, or elapsed indication. For the large .etl/.zip formats this page advertises, it can run for minutes stuck on one line, indistinguishable from a hang.
- **Recommendation:** Feed streaming/numeric progress (rows scanned, current file, phase label) into this page as the RunSearchPage/MainWindow flows do, instead of a bare indeterminate ring.

**Run Search page shows no pre-run summary of what will be searched**
- **Screen:** RunSearchPage — "Search Summary" TextBlock
- **Heuristic:** Recognition over recall / visibility of system status
- **Why it hurts:** The summary box is empty until *after* a run. Before running there is no display of configured locations, rule files, or scope, so a newcomer must recall from other screens what they set up and take it on faith.
- **Recommendation:** Populate the summary on page load with current pipeline state ("2 locations · 0 rule files · depth: Normal" with location names). Show "No sources configured — add one first" when empty, linking to Locations.

**First load dumps the entire UI at once: full toolbar + expanded dense filter panel** _(flagged by 2 reviewers)_
- **Screen:** NativeResultsPage — toolbar, FiltersPanel, ResultsGrid
- **Heuristic:** Aesthetic & minimalist design / cognitive load
- **Why it hurts:** `FiltersToggle IsChecked="True"` expands the whole left-docked panel on first paint (7 time-preset chips + custom range, five field filters, the "Known" toggle, a level legend, and two clear buttons), while the toolbar simultaneously shows ~10 controls plus overflow, and the grid renders behind it. It's a wall of controls competing with results the moment the first search returns.
- **Recommendation:** Default the filter panel collapsed for a new profile (the search box covers the common case). Show most-used filters (search + Level + time presets) by default and tuck advanced controls (Known toggle, per-field boxes, custom range) behind a "More filters" disclosure.

**Results grid opens with ~19 mostly-ETW columns, overwhelming for a text-log open**
- **Screen:** NativeResultsPage — ResultsGrid
- **Heuristic:** Aesthetic & minimalist / information density
- **Why it hurts:** 19 columns including ActivityId, RelatedActivityId, ProviderGuid (240px each), Keywords, OpCode, Channel, RecordId, and Raw Row (500px). For a plain .log/.txt (the default quick action) nearly all are empty, producing a wide horizontal scroll of blank columns that buries Time/Message. Column visibility is hidden behind a "Columns ▾" flyout.
- **Recommendation:** Default the visible column set to the loaded source type (Time/Level/Message/Source/Provider for text logs; reveal ETW columns only when ETW/EVTX data is present), or auto-hide columns entirely empty for the current result set.

**Top-docked filter row does not wrap and has no horizontal scroll — fields clip off-screen**
- **Screen:** NativeResultsPage — FilterRowPanel when "Filters on top" is chosen
- **Heuristic:** Flexibility & efficiency / visibility of system status
- **Why it hurts:** In top dock, FilterRowPanel is a plain horizontal StackPanel of fixed-width fields hosted in a ContentControl (TopFilterHost) with no ScrollViewer, unlike the left-dock host. On a narrow/half-width window the right-most fields (Source, Level) run past the edge, unreachable. The time/level rows use WrapPanelLite and reflow; this row does not.
- **Recommendation:** Use WrapPanelLite for FilterRowPanel in top dock too (matching the time/level rows), or wrap TopFilterHost in a horizontal ScrollViewer so no field becomes unreachable.

**Kernel provider is silently pre-unchecked, so the default "Load selected" quietly drops kernel data**
- **Screen:** Triage "Choose what to load" dialog (`PopulateRows`)
- **Heuristic:** Visibility of system status / error prevention
- **Why it hurts:** `IsChecked = !isKernel` pre-unchecks "Windows Kernel"/"MSNT_SystemTrace." The header only says "uncheck what you don't need" and never mentions a provider is already unchecked. A user who trusts the dialog and clicks "Load selected" gets a scoped load that silently excludes all kernel events, with no indication data was dropped.
- **Recommendation:** Make the pre-exclusion visible — a header note ("The high-volume Windows Kernel provider is unchecked by default — check it if you need kernel events") or a badge on the kernel row explaining why it starts unchecked.

**Unchecking every provider and pressing "Load selected" silently loads the entire file**
- **Screen:** Triage "Choose what to load" dialog
- **Heuristic:** Error prevention / visibility of system status
- **Why it hurts:** A scope is only staged when `selected.Count > 0 && selected.Count < total`. Unchecking everything (intending to narrow/load nothing) and clicking the primary "Load selected" loads the whole multi-hundred-MB file with no scope — the exact slow full load triage exists to avoid — with zero warning the selection was ignored.
- **Recommendation:** When zero providers are checked, disable "Load selected" (or show "Select at least one provider"). Never treat an empty selection as an implicit "load everything."

**Filter inputs lack accessible names; labels are visual-only** _(flagged by 2 reviewers)_
- **Screen:** NativeResultsPage — Provider/TaskName/Message/Source filter boxes and known-value combos
- **Heuristic:** Accessibility (labels) / Consistency
- **Why it hurts:** The field labels are separate TextBlocks not programmatically associated with their inputs. TaskNameFilterBox, MessageFilterBox, SourceFilterBox and the combos carry no AutomationProperties.Name; ProviderFilterBox has an AutomationId but no Name. Their only accessible text is PlaceholderText, which disappears once the user types — so a screen-reader user tabbing back through filled filters hears nothing identifying the field, inconsistent with the toolbar buttons.
- **Recommendation:** Add AutomationProperties.Name (or LabeledBy pointing at the adjacent label) to every filter TextBox, ComboBox, and multi-select DropDownButton so each announces its field regardless of contents.

**Icon-only chrome buttons have tooltips but no accessible name** _(flagged by 2+ reviewers)_
- **Screen:** MainWindow top-right/status strip (McpHelpButton, StatusEditButton, StatusHideButton) + NativeResultsPage output-files dismiss button
- **Heuristic:** Accessibility (labels) / Consistency
- **Why it hurts:** The app sets AutomationProperties.Name on ~75 buttons, but several icon-only ones rely on ToolTipService.ToolTip alone, which is not a reliable accessible name. A screen-reader user tabbing the top bar hits unlabeled "button" controls beside labeled ones.
- **Recommendation:** Add AutomationProperties.Name to every icon-only button ("MCP help," "Choose status bar items," "Hide status bar," "Dismiss output files banner"), matching the tooltip. Consider a shared icon-button style that enforces a name.

**Diagnostics are fragmented across unrelated menus with no single home**
- **Screen:** SystemInfoPage / LogsPage / SearchStatisticsPage / AboutPage / ResultsViewerSettingsPage (Logs tab)
- **Heuristic:** Recognition over recall / minimalist design
- **Why it hurts:** "System Check" and "Logs" are under Settings; "Statistics & timing" is under Run & Results; "Export performance report" is on About; "Send app logs to dev" is buried in Preferences → Logs. A user troubleshooting or filing a bug has no single obvious place.
- **Recommendation:** Group these under one "Diagnostics"/"Troubleshooting" section: System Check, Logs, Search statistics, Send logs to dev, Export performance report. At minimum cross-link them.

**Pervasive undefined jargon ("RuleDSL," "Pipeline Sections," scope/filter/enrichment purposes) with no help**
- **Screen:** SearchRulesPage ("Rule files" tab)
- **Heuristic:** Match to the real world / Help & documentation
- **Why it hurts:** Title "⚙️ RuleDSL Configuration," subtitle "Manage rule files and pipeline sections," header "📋 Pipeline Sections," and a purpose filter (All/Scope/Filter/Enrichment/UML/Output) — none explained, no Help link, bare Type/Rules/Source column headers. A newcomer has no idea what any of it means or why to care.
- **Recommendation:** Lead with plain-language framing ("Rules automatically tag, filter, and enrich your search results") and add an inline learn-more link (repo already has FindNeedleRuleDSL/README.md). Add one-sentence tooltips to the purpose values or rename them to user-facing terms.

**Page headers use three sizes, two weights, and inconsistent emoji/capitalization**
- **Screen:** All configuration/result pages
- **Heuristic:** Consistency & standards / cognitive load
- **Why it hurts:** No rule governs headers: "📁 Search Locations" (24 Bold, emoji, Title Case) vs "Auto-add rules" / "Active rules" (24 SemiBold, no emoji, sentence case) vs "About FindNeedle" / "Appearance" (22 SemiBold) vs "Connections" (26 SemiBold). The inconsistency reads as unpolished and increases cognitive load.
- **Recommendation:** Define one shared page-title style (single size + weight) and one capitalization convention (recommend sentence case, the majority) via a StaticResource or a small header user-control. Decide emoji in/out and apply uniformly.

**Main settings page is labeled "viewer" only, but holds app-wide settings** _(flagged by 2 reviewers)_
- **Screen:** ResultsViewerSettingsPage (Settings → Preferences…)
- **Heuristic:** Match to the real world / Consistency
- **Why it hurts:** Menu says "Preferences...", PaneTitle says "Viewer settings," and the subtitle asserts everything applies to "the native results viewer" — yet the page hosts MCP server enable+port, File associations, storage backend, parallel ingest, cache reuse, WPP decoding, status-bar and Welcome toggles. A user hunting for "file associations" or "MCP" would never look under "Viewer settings."
- **Recommendation:** Align the menu item, pane title, and subtitle on one name (e.g. "Settings") and reword the subtitle so it doesn't claim everything is viewer-scoped; scope the viewer wording to the Appearance/Columns panels only.

### Low

**Level legend chips look clickable but are inert; filtering by level is a separate combo** _(flagged by 2 reviewers)_
- **Screen:** NativeResultsPage — LevelsRowPanel legend vs LevelFilterCombo
- **Heuristic:** Recognition over recall / Consistency / false affordance
- **Why it hurts:** The legend renders colored, bordered, count-bearing chips styled identically to the interactive Time-preset toggles, but has no Click handler ("Display-only"). The actual filter is a separate "Level (all)" combo. A newcomer will almost certainly click a chip to filter and get nothing.
- **Recommendation:** Either make the chips the real filter control (click to toggle/multi-select by level, matching Time presets) or restyle them as plainly non-interactive labels (no border/hover, labeled "Level counts"). Don't ship two look-alike level controls where only one works.

**No indication that filters are active once the filter pane is collapsed**
- **Screen:** NativeResultsPage — Filters toggle button
- **Heuristic:** Visibility of system status / error prevention
- **Why it hurts:** `FiltersToggle_Click` just flips expand state with no badge or active-count. A user can set a filter, collapse the pane, and the grid silently shows fewer rows with the only cue buried in StatusText.
- **Recommendation:** Show an active-filter indicator on the toggle when any per-column/time/level filter is set (accent dot or "Filters (3)").

**"No results" / empty state assumes a search always ran; no first-run guidance or next step** _(flagged by 3 reviewers)_
- **Screen:** NativeResultsPage — EmptyOverlay (`UpdateEmptyState`)
- **Heuristic:** First-run empty states / help users recover
- **Why it hurts:** Only two branches exist: "No matching rows" (filters) and "No results" ("the source was empty, or nothing matched/decoded"). There is no "nothing loaded yet" state, so a newcomer who reaches the grid with no data is wrongly told a search "returned no rows," and the only action (Clear filters) is hidden unless filters are active. Zero forward path.
- **Recommendation:** Add a third empty-state variant for the fresh/no-data case ("No log loaded yet") with a primary action navigating to the search/source page. Only use "returned no rows" when a search actually executed, and give even that state an "Open a log file"/"Add a source" action (plus a "Fix decoding" link when a decode warning exists).

**Online-source buttons (Kusto/ADO/GitHub) dead-end on first run instead of adding anything**
- **Screen:** SearchLocationsPage → New pivot → "Online sources"
- **Heuristic:** Error prevention / recognition over recall
- **Why it hurts:** With no saved connections, each handler bails at `conns.Count == 0` and pops PromptNoConnectionsAsync — the buttons never add a source, only redirect. They look actionable but are functionally disabled for exactly the newcomer most likely to click them.
- **Recommendation:** When no connection of that kind exists, change the affordance before the click — label it "Set up Kusto…" or show an inline hint, and route directly into connection creation, returning to add the location afterward.

**"Configured Locations" list has no empty state — the primary page looks blank/broken on first run**
- **Screen:** SearchLocationsPage — "Configured Locations" section
- **Heuristic:** Visibility of system status / empty states
- **Why it hurts:** The header is followed by an empty ItemsRepeater with no placeholder. On a fresh profile the user sees a bold header with nothing beneath — inconsistent with RecentEmpty ("No recent locations yet…") and ConnectionsPage's EmptyHint on the same feature, so it reads as a rendering bug.
- **Recommendation:** Add an empty-state TextBlock under the header ("No locations added yet — use New above to add a folder, file, or live Event Log"), styled to match RecentEmpty/EmptyHint.

**"Rule files" tab has no empty state or way to create a first rule**
- **Screen:** SearchRulesPage — RuleFilesListBox / RuleSectionsListView
- **Heuristic:** Empty states / onboarding; error prevention
- **Why it hurts:** On a fresh profile both lists render as blank boxes with header rows and no placeholder. The only affordance is "📂 Browse..." for a *.json file the user has never seen — no template, sample, or format explanation.
- **Recommendation:** Add an empty-state panel: one-line explanation, a "Create a starter rule file"/"Add a sample" button that writes a template, and a link to the format docs. Show placeholder text in the empty lists.

**Rules hub opens on the empty "Active" runtime-status tab, not an actionable config tab**
- **Screen:** RulesPage → default tab SearchProcessorsPage
- **Heuristic:** Empty states / first-run onboarding
- **Why it hurts:** `_initialTag = "active"` defaults to SearchProcessorsPage, which shows "No active rules…" whenever LastRuleProcessors is empty — always true before any search. The first thing a newcomer sees in the Rules hub is an empty diagnostic pane, and "Active" is the first nav tab, ahead of the config tabs.
- **Recommendation:** Default the hub to a config tab ("Rule files" or "Auto-add") on first-run/empty, or reorder the nav. Reserve "Active" for after a search runs; when no rules are configured, replace the empty InfoBar with a "what are rules / add your first rule" call-to-action.

**Two "add data" surfaces (Search Locations vs Log Finder) with no explanation of which to use**
- **Screen:** SearchLocationsPage vs LogFinderPage
- **Heuristic:** Recognition over recall / cognitive load
- **Why it hurts:** LogFinderPage.OpenEntry calls `NewWorkspace()` then runs immediately, while SearchLocationsPage appends to the current workspace. A newcomer can't know Log Finder starts a brand-new search-and-run while Search Locations accumulates sources for the current one; neither page cross-references the other.
- **Recommendation:** Clarify each subtitle (Log Finder: "Quick-open a known log location as a new search"; Search Locations: "Build up the set of sources for the current search"), or unify them into one consistent action.

**Two disconnected search flows (quick-open vs build-a-pipeline) with no explanation**
- **Screen:** WelcomePage Quick Actions vs Configure → Run & Results
- **Heuristic:** Consistency & standards / cognitive load
- **Why it hurts:** Quick actions (open_file, open_folder, open_rules, cached) go straight to the viewer, never touching Run Search — but the intro banner presents the Configure→Run pipeline as an equal path. A newcomer can't tell whether Open File is a complete search or whether they still need to Run.
- **Recommendation:** Pick one primary path. Frame the quick-open tiles as the fast path ("Open a log — results appear") and subordinate the Configure→Run pipeline as advanced. In the banner, lead with "Click Open Log File below" and collapse pipeline instructions behind a "Building a custom pipeline?" expander.

**First-screen intro banner is dense and navigates by prose instead of pointing**
- **Screen:** WelcomePage IntroBanner
- **Heuristic:** Aesthetic & minimalist design / recognition over recall
- **Why it hurts:** The first thing a newcomer reads is a two-column InfoBar mixing getting-started prose with a four-item feature list, routing via textual menu paths ("Configure ▸ Locations…", "the Run → View Results button") and front-loading jargon (RuleDSL, ETW, enrichment) before the user has acted.
- **Recommendation:** Trim to a single "Start here" sentence tied to the Quick Action tiles below. Move the feature list and jargon to the docs link or a "Learn more" expander.

**Prominent green "Run → View Results" button is a dead-end for a fresh profile**
- **Screen:** MainWindow status strip (run_view)
- **Heuristic:** Visibility of system status / error prevention
- **Why it hurts:** The shell's single most prominent CTA hits `if (Locations.Count==0)` and silently navigates to SearchLocationsPage — it looks runnable but can't run, and clicking teleports with no explanation. (Same root issue as the medium "Running with no sources" finding, from the status-bar side.)
- **Recommendation:** Disable or de-emphasize Run→View until a location exists, and/or show an inline hint ("Add a location first") instead of a silent page switch.

**Results search box front-loads query DSL syntax at a first-time user**
- **Screen:** NativeResultsPage — SearchBox
- **Heuristic:** Match to the real world / minimalist
- **Why it hurts:** PlaceholderText is "Search… or query: msg ~ error AND level == Warning." The DSL operators (~, ==, AND) are presented as if expected, which can intimidate a user who just wants to type a word, even though plain text works.
- **Recommendation:** Simplify to "Search all columns…" and surface advanced syntax only via the existing "?" flyout or a subtle "Advanced query" affordance.

**Query help uses abbreviated field tokens that don't match visible column/filter names**
- **Screen:** NativeResultsPage — SearchBox tooltip and "?" syntax flyout
- **Heuristic:** Match to the real world / terminology
- **Why it hurts:** The help teaches "msg, taskname, provider, pid, tid, eventid…" but the columns/filters are "Message, TaskName, ProcessId, ThreadId, EventId." "ProcessId → pid" and "ThreadId → tid" aren't guessable; the mapper accepts full names but the help never says so.
- **Recommendation:** List tokens using the words the UI shows (message, processid, threadid, eventid) or note the aliases explicitly ("message (msg), processid (pid)…").

**Primary content (Message) is clipped by fixed 26px row height with no visible expand affordance**
- **Screen:** NativeResultsPage — ResultsGrid Message column
- **Heuristic:** Recognition over recall (discoverability)
- **Why it hurts:** RowHeight=26 with no wrapping truncates the Message column at the cell edge. Full text only appears when a row is selected (details mode), but nothing signals a row is expandable — no chevron or hint — so a newcomer scanning truncated log lines may never discover the details view.
- **Recommendation:** Add a subtle affordance (expander glyph or a one-time "click a row for full details" hint) and/or a hover tooltip showing the full Message text.

**General Help is buried in the overflow "…" menu**
- **Screen:** NativeResultsPage — OverflowButton menu
- **Heuristic:** Help & documentation
- **Why it hurts:** The only always-visible help is the "?" button, scoped to query syntax. General Help and Settings live inside the unlabeled "…" overflow flyout a newcomer must first discover.
- **Recommendation:** Surface Help at the top level (or make "?" link to full docs) and give the overflow button a clearer affordance/label.

**Filter-performance button shows cryptic metric text as a permanent toolbar item**
- **Screen:** NativeResultsPage — FilterPerfButton
- **Heuristic:** Match to the real world / terminology
- **Why it hurts:** Developer-facing timing text (bound to LastFilterSummary, Consolas breakdown flyout) sits inline in the main toolbar with no explanatory label, adding jargon next to the primary actions.
- **Recommendation:** Move filter-performance detail into the overflow/diagnostics area (or hide until a filter is slow) and give it an explicit label/icon.

**Two near-identically named clear buttons with different scopes in the same pane**
- **Screen:** NativeResultsPage — "Clear filters" (field row) vs "Clear all filters" (bottom)
- **Heuristic:** Consistency & standards / error prevention
- **Why it hurts:** "Clear filters" clears only per-field boxes; "Clear all filters" also clears the search box, time range, and level. They sit ~150px apart with nearly identical wording — a newcomer can't predict which resets what.
- **Recommendation:** Differentiate scope in wording ("Clear field filters" vs "Reset everything (incl. search & time)"), or collapse to one Clear with a small menu ("Fields only" / "Everything").

**"Known ▾" toggle label is opaque and its caret implies a menu that doesn't exist**
- **Screen:** NativeResultsPage — ShowKnownToggle
- **Heuristic:** Recognition over recall / match to the real world
- **Why it hurts:** "Known" is jargon for "values present in the loaded log," explained only in a tooltip. The ▾ glyph signals a dropdown, but clicking silently swaps the Provider/TaskName/Source boxes for combos. A newcomer can't recognize this as where facet/pick-list filtering lives.
- **Recommendation:** Rename to self-describing text ("Pick from values" / "Choose values ▾") and drop the misleading caret, or make it a real menu. Add a one-time inline hint.

**No "Select all / Deselect all" in a provider list that can hold dozens of entries**
- **Screen:** Triage "Choose what to load" dialog
- **Heuristic:** Flexibility & efficiency of use
- **Why it hurts:** One CheckBox per provider (possibly dozens), capped at 360px scroll; the only bulk affordance is a filter box that hides but doesn't toggle. To load 2 of 40 providers a user unchecks 38 boxes one at a time.
- **Recommendation:** Add "Check all"/"Uncheck all" (ideally scoped to filtered rows, e.g. "Check all matching") so the common "just one or two loggers" workflow is a couple of clicks.

**The per-provider count column is unlabeled — new users can't tell what the numbers mean**
- **Screen:** Triage dialog — count column
- **Heuristic:** Recognition rather than recall
- **Why it hurts:** Each row shows a right-aligned dimmed number with no header or unit; the "counts are approximate/partial" caveat only appears in warnText when sampled.
- **Recommendation:** Label the column ("Events" header or " events" tooltip) and surface the "approximate/partial" note whenever the list came from a bounded scan.

**"Inspect ETL" jumps straight to a file picker with no explanation, inconsistent with "Inspect Binary"**
- **Screen:** Inspect menu → Inspect ETL
- **Heuristic:** Consistency & standards / Help & documentation
- **Why it hurts:** InspectEtlAsync opens the file dialog immediately with no preamble, while InspectBinaryAsync first shows a ContentDialog describing what it reads. Two sibling "Inspect" items behave inconsistently, and Inspect ETL gives no clue about its output.
- **Recommendation:** Give Inspect ETL a short intro consistent with Inspect Binary ("Reads a summary of an .etl — Windows build, capture window, event/lost counts, and providers — without a full load"), or a menu tooltip.

**Cache-reuse prompt uses insider terms and silently defaults to reuse**
- **Screen:** CacheReusePromptService — "Use cached results?" dialog
- **Heuristic:** Match to the real world / help users recover
- **Why it hurts:** A mid-search dialog offers "Use cache" / "Rescan" (default Use cache). A newcomer has no model of an internal scan cache, so it's ambiguous whether "Use cache" means stale data. On any failure path Prompt() returns true and silently reuses the cache with no notice.
- **Recommendation:** Reword to plain outcomes ("Open previous results (fast)" vs "Rescan now (up to date)"), keep the age/row details, consider making Rescan the default for older caches, and surface a small "reused previous scan" note when the prompt was skipped.

**Two "run a search" entry points with inconsistent design and terminology**
- **Screen:** RunSearchPage vs QuickLogWithRulesPage
- **Heuristic:** Consistency & standards
- **Why it hurts:** RunSearchPage (emoji header, Shallow/Normal/Deep radios, two raw ProgressBars, jargon "(scanned)"/"(from cache)") vs QuickLogWithRulesPage (clean numbered wizard, placeholder hints, live rules validation, a single ProgressRing). Same task, inconsistent vocabulary, feedback widgets, and mental models.
- **Recommendation:** Standardize the run experience — shared progress/loader component, shared status wording, consistent heading style. Reuse QuickLog's clearer stepwise+validation pattern on RunSearchPage.

**Auto-add "Edit condition" dialog relies on recall of magic strings with no validation or pickers**
- **Screen:** AutoAddRulesPage → Edit condition dialog
- **Heuristic:** Error prevention / recognition over recall
- **Why it hurts:** Free-text boxes for "Source kinds (ETW, EventLog, Folder, File, Zip)," path globs, provider names, min/max build, stored verbatim with no validation. A typo like "Eventlog"/"EWT" is accepted and the rule silently never matches. New rules start with a blank condition, so the user must already know the exact tokens.
- **Recommendation:** Replace source-kind free text with a multi-select of known kinds, validate/normalize on Save (reject/warn on unknown tokens), show click-to-insert examples, and consider a live "would this match your loaded logs" preview.

**Field-extraction enrichment toggle is explained with a dense expert wall of text**
- **Screen:** ReformatRulesPage ("Field extraction" tab)
- **Heuristic:** Aesthetic & minimalist design / cognitive load
- **Why it hurts:** The explanatory paragraph packs scan-time cost, separate cache, rescan warmth, and MCP server into one block a newcomer can't parse; the intro also demands understanding regex named groups up front.
- **Recommendation:** Reduce to a one-line benefit ("Turn extracted fields into sortable/filterable columns") with a "Learn more" expander for the performance/cache/MCP details. Keep regex specifics inside the Add/Try-it flow.

**Feature naming is inconsistent ("Field extraction" / "reformat rule" / "message-reformat"), and per-rule toggles lack accessible labels**
- **Screen:** ReformatRulesPage / RulesPage nav / AutoAddRulesPage rows
- **Heuristic:** Consistency & standards; accessibility
- **Why it hurts:** The nav/title say "Field extraction," the dialogs say "reformat rule," the class doc says "message-reformat rules" — three names for one feature. Separately, AutoAddRulesPage per-row ToggleSwitch has empty On/OffContent and no AutomationProperties.Name, so a screen reader hears no label tying the switch to its rule.
- **Recommendation:** Pick one user-facing name ("Field extraction") across tab/page/dialog. Bind AutomationProperties.Name on the per-row toggle to the rule name.

**"Logs" page is a bare toggle + empty list with no title, description, or empty state**
- **Screen:** LogsPage (Settings → Logs)
- **Heuristic:** Aesthetic & minimalist (empty state) / Help & documentation
- **Why it hurts:** No page heading or description — just a "Debug Logging" toggle (default off), Copy/Clear buttons, and an empty ListView. A newcomer sees an empty box with no explanation of what the view is or that turning Debug on populates it.
- **Recommendation:** Add a title and one-line description ("Internal app diagnostic log — turn on Debug Logging to capture detailed events") and an empty-state message ("No log entries yet. Enable Debug Logging to capture more detail.").

**Two different "Logs" concepts under Settings; the Preferences "Logs" tab shows no logs**
- **Screen:** ResultsViewerSettingsPage (Logs tab) vs Settings → Logs menu item
- **Heuristic:** Consistency & standards / match to the real world
- **Why it hurts:** The Settings menu "Logs" opens the live log viewer, but the Preferences page also has a "Logs" category containing only "Send app logs to dev…" and the settings-file path — no log content. A user clicking Preferences → Logs expecting logs finds none.
- **Recommendation:** Rename the Preferences category to "Diagnostics"/"Support" so it doesn't collide with the log-viewer page, and/or link it to the actual Logs page.

**System Check flags missing tools but offers no way to fix them**
- **Screen:** SystemInfoPage (Settings → System Check)
- **Heuristic:** Help users recover from errors
- **Why it hurts:** Health list shows "✗ missing" with only a "Re-check" button. When PlantUML/Java or Mermaid/Node is missing, there's no inline remediation — the user must independently find the separate Diagram Tools page with the Install buttons.
- **Recommendation:** For each failing check, surface an action link ("Install…" → Diagram Tools, "Set TMF path…" → Decoding settings) so the user can act from where the problem is reported.

**The same three online providers are added through two divergent UIs (labels + icons)**
- **Screen:** SearchLocationsPage vs ConnectionsPage
- **Heuristic:** Consistency & standards
- **Why it hurts:** SearchLocationsPage uses one cloud glyph and "Add ADO"; ConnectionsPage uses per-provider emoji (🔷/🐙/🔎) and full names. So the same action is "Add ADO" (cloud) in one place and "Add Azure DevOps" (diamond) in another; the Kusto magnifier also collides with the app's search theme.
- **Recommendation:** Standardize labels (always spell out "Azure DevOps") and one icon per provider used identically on both pages. Share a single provider-button component/data list so they can't drift.

**Button reads "Add ADO" — jargon abbreviation inconsistent with the spelled-out label elsewhere**
- **Screen:** SearchLocationsPage → New pivot online sources button
- **Heuristic:** Match to the real world / consistency
- **Why it hurts:** The button says "☁ Add ADO" while ConnectionsPage says "Add Azure DevOps" for the same feature; "ADO" is internal shorthand a newcomer won't recognize, and its own AutomationProperties.Name already says "Add Azure DevOps location."
- **Recommendation:** Use "Add Azure DevOps" on the button (widen the fixed 120px width as needed).

**Connections add-buttons rely on emoji with no accessible name (inconsistent with Search Locations)**
- **Screen:** ConnectionsPage → Add Azure DevOps / GitHub / Kusto
- **Heuristic:** Accessibility (labels) / consistency
- **Why it hurts:** These buttons contain only an emoji TextBlock plus text and set no AutomationProperties.Name, so a screen reader reads the raw emoji. SearchLocationsPage's equivalents all set explicit names and mark the emoji decorative.
- **Recommendation:** Mark the emoji `AutomationProperties.AccessibilityView="Raw"` and/or set explicit AutomationProperties.Name for parity.

**Two competing icon systems: color emoji vs monochrome Fluent icons**
- **Screen:** Search Locations, Connections, Run Search vs Welcome/viewer
- **Heuristic:** Consistency & standards / accessibility
- **Why it hurts:** Some screens use colorful emoji as glyphs ("➕ Add Folder", "☁ Add Kusto", "🚀 Run Search") while others use monochrome SymbolIcon/FontIcon for the same concepts. Emoji render differently across themes and are announced literally by screen readers ("rocket Run Search").
- **Recommendation:** Standardize on the Fluent/Segoe icon set for action affordances and remove decorative emoji from buttons/radios/titles — or, if emoji are deliberate, apply them consistently and mark them AccessibilityView="Raw".

**Product name spelled two ways: "FindNeedle" and "Find Needle"**
- **Screen:** App-wide (title bar, Welcome, About, Settings, Explorer integration)
- **Heuristic:** Consistency & standards
- **Why it hurts:** The window title and Explorer-integration strings say "Find Needle," while the Welcome hero, About, and body copy say "FindNeedle." A newcomer sees both spellings on first launch (title bar vs Welcome hero).
- **Recommendation:** Choose the canonical spelling (the wordmark uses "FindNeedle") and sweep the window Title and Explorer-integration strings to match.

**All three Search Depth options share the same magnifier icon**
- **Screen:** RunSearchPage — Search Depth radio group
- **Heuristic:** Aesthetic & minimalist design
- **Why it hurts:** "🔍 Shallow", "🔍 Normal", "🔍 Deep" all use the identical glyph, carrying no differentiating information and adding noise.
- **Recommendation:** Drop the redundant emoji (text alone is clear), or use three distinct glyphs. (Also see the medium "Deep is a no-op" finding — ideally this control is reduced anyway.)

**Hidden status bar has no on-screen way to bring it back**
- **Screen:** MainWindow status strip (StatusHideButton)
- **Heuristic:** User control & freedom / help users recover
- **Why it hurts:** The 11px "×" hide button sits immediately beside the gear edit button; one misclick removes the whole navigation/run surface (Locations/Rules/Last run/Run→View), and the only recovery is buried in Settings ▸ Preferences ▸ "Show status bar" — undiscoverable to a newcomer.
- **Recommendation:** Move "×" behind the gear/overflow menu (not a one-click sibling of edit), confirm before hiding, or leave a thin persistent restore handle when the strip is hidden.

**"MCP" toggle occupies prime top-right real estate with pure jargon**
- **Screen:** MainWindow top-right (McpToggle / McpHelpButton)
- **Heuristic:** Aesthetic & minimalist design / match to the real world
- **Why it hurts:** The only thing in the top-right is a ToggleButton labeled "MCP" plus a "?". A newcomer has no idea what an AI-agent server is; the unexpanded acronym dominates the header and competes with the actual first task.
- **Recommendation:** Move the MCP toggle into Settings (or an advanced/status-bar chip), or at minimum spell it out ("AI agent server (MCP)").

**First menu "Quick" is an ambiguous label that hides the primary get-started actions**
- **Screen:** MainWindow MenuBar (QuickMenu) + WelcomePage Quick Actions
- **Heuristic:** Recognition over recall / match to the real world
- **Why it hurts:** The leftmost menu is "Quick" — an interaction speed, not a content category — yet it holds Welcome + Open File/Open Folder/Open with Rules, the actual entry points. A newcomer can't predict its contents by label.
- **Recommendation:** Rename to "Open" or "Start," or fold home + open actions under an obvious "File"/"Open" menu.

**Menu bar and status-strip icon buttons lack keyboard mnemonics and accessible names**
- **Screen:** MainWindow MenuBar + StatusEditButton / StatusHideButton
- **Heuristic:** Accessibility (labels, keyboard)
- **Why it hurts:** No MenuBarItem/MenuFlyoutItem defines AccessKey/KeyboardAccelerator, so there are no Alt-mnemonics for the top-level menus; the gear/hide icon buttons set only tooltips, not AutomationProperties.Name, so a screen reader announces unnamed buttons.
- **Recommendation:** Add AccessKey mnemonics to top-level menus and AutomationProperties.Name to the gear/hide (and other icon-only chrome) buttons.

**Status strip shows a meaningless "Last run: —" segment before any search**
- **Screen:** MainWindow status strip (lastrun item)
- **Heuristic:** Aesthetic & minimalist design / cognitive load
- **Why it hurts:** On a fresh profile the segment renders "Last run: —" and clicking it opens an empty viewer — a no-value segment and an action leading nowhere for a user who hasn't searched.
- **Recommendation:** Suppress the Last-run segment (return null) until a search has produced results, mirroring how "filters"/"outputfiles" return null at zero count.

**Developer/QA affordance "Preview first-run (new user)…" is exposed to every user**
- **Screen:** MainWindow Settings menu (preview_new_user)
- **Heuristic:** Aesthetic & minimalist design / cognitive load
- **Why it hurts:** This spawns a second app instance against a throwaway temp profile — an internal UX-testing tool that confuses a real newcomer (why did a second window open?) and adds noise to Settings.
- **Recommendation:** Gate behind a debug/developer flag or remove from the shipped Settings menu.

**Automation/test hook ("Test file path" box + "🚀 Load") is exposed in production UI beside Browse**
- **Screen:** SearchRulesPage — TestFilePathInput / TestLoadButton
- **Heuristic:** Aesthetic & minimalist design / cognitive load
- **Why it hurts:** A "Test file path" TextBox and "🚀 Load" button (backing the documented `AddRuleFileByPath` test hook) sit next to "📂 Browse...", duplicating it and inviting confusion (why type a raw path when Browse exists?).
- **Recommendation:** Remove the test-path box/Load button from shipped UI (keep `AddRuleFileByPath` for automation), or hide behind a debug flag; if manual entry is wanted, merge into one labeled "Add by path" affordance.

**Inconsistent visual language: emoji-laden "Rule files" tab vs clean Fluent siblings in the same hub**
- **Screen:** SearchRulesPage vs AutoAddRulesPage / ReformatRulesPage / SearchProcessorsPage
- **Heuristic:** Consistency & standards
- **Why it hurts:** SearchRulesPage uses emoji in nearly every label ("⚙️ RuleDSL Configuration", "📂 Browse...", "🗑️ Remove Selected", "🚀 Load") while sibling tabs in the same NavigationView use clean Fluent FontIcon glyphs and plain text — jarring within one hub.
- **Recommendation:** Replace the emoji with the same FontIcon/SymbolIcon vocabulary used by the other rule pages.

## 4. Quick wins

- **Add missing empty states** using the wording/style already present in RecentEmpty/EmptyHint: Configured Locations, Rule files, Logs page, and a new "No log loaded yet" variant in the results EmptyOverlay (with an "Open a log file"/"Add a source" action).
- **Fix the "Deep (thorough)" depth radio** — remove it (or wire it to real behavior) so a visible control isn't a silent no-op; drop the three identical magnifier emoji while you're there.
- **Add a zero-source guard** shared by RunSearchPage and the status-bar Run buttons: an inline "Add a source first" InfoBar with an action, instead of silent redirect or running over nothing. Disable/de-emphasize the green "Run → View Results" until a location exists.
- **Simplify the results SearchBox placeholder** to "Search all columns…" and move DSL syntax into the "?" flyout.
- **Default the results filter panel to collapsed** for a new profile, and add an active-filter badge/count on the Filters toggle.
- **Resolve the level legend false affordance** — either make the chips filter on click or restyle them as plainly non-interactive "Level counts."
- **Add AutomationProperties.Name** to icon-only chrome buttons (McpHelp, StatusEdit, StatusHide, output-files dismiss) and to every per-column filter input — matching the tooltip/label text.
- **Rename "Add ADO" to "Add Azure DevOps"** and align the Preferences page name/subtitle (menu says "Preferences," pane says "Viewer settings") on one term.
- **Normalize the product name** to "FindNeedle" in the window title and Explorer-integration strings.
- **Move the "MCP" toggle and "Preview first-run (new user)…"** out of the top chrome / shipped Settings menu (spell out MCP or gate behind an advanced flag), and suppress the "Last run: —" status segment until a search runs.
- **Add "Check all / Uncheck all"** to the triage provider dialog, disable "Load selected" when nothing is checked, and add a header note explaining the pre-unchecked kernel provider.
