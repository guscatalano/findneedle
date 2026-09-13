# UX follow-ups (branch `ux-redesign`)

What is left after the redesign pass, in the order we agreed to do it. One commit per item;
tick the box and note the hash when it lands. Anything that turns out bigger than described gets
split rather than stretched.

Status key: `[ ]` not started · `[~]` in progress · `[x]` done (hash) · `[-]` dropped (why)

## 1. Small bugs and small gaps

- [x] (feb5f0e) **Recent searches flags folder sources as "source missing".** `CachedSearchCatalog` checks
      `File.Exists` only, so every folder source reads as missing on the Recent page. Home already
      checks directories; the page does not. Check both.
- [x] (2b9040e) **Open-policy Ask dialog names a known log by its folder** ("Add Logs to it…" for the Event Log
      folder). `OpenIntoWorkspaceAsync` takes a `displayName`; the Known logs paths don't pass one.
      Pass the catalog entry's name.
- [x] (41a4019) **Added field filters are forgotten on restart.** Persist the list of added fields in
      `viewer-settings.json` (session-only today). Defaults stay Provider/TaskName/Message/Source.
- [x] (3cb4799) **Last-run line says "(cached)".** Reword to "from cache" / "scanned". Keep the qualifier; it
      answers a real question.

## 2. Investigate before trusting

- [x] (explained, no code change) **Cache-reuse prompt never appeared** when opening the Windows Update
      known log. Not a regression. The cache was valid and complete (`cache.write.ok rows=71437`); the
      NEXT open logged `cache.eval reuse=false reason=size_differs got=27918336 want=27967488`. The
      folder is live — Windows Update kept writing to it — so the size+mtime signature correctly
      declared the cache stale, and the prompt only appears on a hit. Working as designed; reopening
      that cache would have shown stale data.
      **Design gap it exposes:** for any actively written log (all the Known logs) the reopen cache
      effectively never hits, and nothing says why. Two candidate improvements, pending decision:
      - [ ] Recent searches row shows "log changed since" (same signature check) so a rescan is
            expected rather than mysterious. Information only.
      - [ ] Offer a stale cache honestly instead of silently wiping it: "Previous results are from
            09:02 and the log has changed since — open previous anyway / rescan". Changes semantics;
            owner's call.

## 3. Things already asked for

- [x] (ea414a6) **Delete the Run page.** "Run search" is an action everywhere now; the page is the last place it
      is a destination, and it is redundant with the shell spinner, the viewer banner and the Home
      card. Remove the page, its PageCatalog entry, the `run_search` quick action and palette entry.
      The backstop on `OpenWithOptionalStreamingAsync` already covers the path everything uses;
      `SearchOrchestrator` and its tests go with the page.
- [x] (9952709) **Follow as a split button.** In the in-row detail: Follow ▾ with this activity / this process /
      this thread / this provider, shaped like Filter in ▾. Only offer items whose field the row
      actually has, so nothing silently does nothing.
- [x] **Raw level in the row details.** The details showed only the mapped Level (Info/Error/…), never
      the number the source recorded. ISearchResult grew `GetRawLevel()` (ETW TraceEventLevel, Event
      Log Level byte, WPP meta level; "" for text logs); it is persisted as a RawLevel column (cache
      schema v11, old caches rebuild) and shown as a RawLevel row under Level in the in-row template,
      the panel and the popup, plus the XML copy and the MCP record. Only sources with a raw value get
      the row. tracefmt-decoded WPP rows have no level in the text, so they stay blank.
- [x] **Home tiles never repeat the cards.** The "Shortcuts" row is now "Tools": the catalog dropped every
      entry that already has a card button (open file / folder / with rules, Known logs, Recent searches,
      Run search, Open results) and keeps only jump-to-a-tool entries (Sources, Rule files, Auto rules,
      Inspect ETL, Diagram tools, Outputs, the three online sources). It stays at the bottom: Home's
      first screen is "get a log in front of you"; tools matter once something is loaded. A stored
      selection made only of retired ids falls back to the defaults; the retired ids still work from the
      palette and MCP.
- [x] **Status strip vs workspace chip.** "1 source · 0 rule files" was rendered four times on Home (title,
      chip, workspace card, strip). Split by what changes when: the chip owns workspace composition (name,
      counts, rename / save / new), the strip owns run state (Run/Stop · Last run · Storage · Outputs, in
      that order), the window title is the name only, the Home card stays as the detailed view. Sources /
      Rule files remain in the strip catalog for anyone who wants them via the pencil; a stored selection
      that is exactly the old default set follows the new defaults, a real customization is kept.
- [x] **A way to cancel a search.** The streaming open (the default) showed "Running search…" with no
      Cancel while a big .etl decoded, and Run ▸ Stop / Esc stayed disabled because nothing refreshed
      the running state. Now: the spinner's Cancel covers both paths, the status-bar Run turns into a
      red Stop while a search runs (no configuration needed), Esc and Run ▸ Stop arm on the streaming
      path too, and a cancelled run stays where it was instead of opening an empty viewer. The engine
      finishes a cancelled scan normally with whatever it had, so the cancel is detected from the token
      and reported as "cancelled (N rows kept)" rather than "0 results (scanned)".

## 4. Finish the review (F3 / F8)

- [x] (2fe4c2d) **Move the Active rules tab** out of the Rules hub into the viewer's Sources dialog (runtime
      status beside "which sources loaded"). Rules hub keeps Rule files / Auto rules / Field extraction.
- [x] (75312b4) **One name for field extraction.** The viewer's "Reformatted — …" label and any "(enrichment)"
      wording become "Field extraction".
- [x] (77596ce) **Regroup Settings** so categories match their contents: Appearance · Viewer (paging, sort, row
      tags, dropdowns, columns) · Loading & cache (progressive, cache, storage, index, cleanup) ·
      Decoding · Integrations · App (welcome, status bar, shortcuts, file associations) · Support.
      Cards move; no behaviour or x:Name changes.

## 5. Merge Diagram tools into Outputs

- [ ] **Outputs becomes the one place for what a run produced** — files and diagrams — as a list with
      a real empty state ("Nothing yet. Rule files with output rules produce files here.") and
      open / reveal per item. Drop the hamburger and the two empty grey boxes.
- [ ] **Diagram tools' installer content** (PlantUML, Mermaid, paths, test, install folder) moves to
      Settings ▸ Integrations. The Desktop Session Replay demo goes to Help or goes away.
- [ ] **Remove the Diagram tools page** and its menu / palette / quick-action entries once the above
      is in.

## 6. Search timing — design first

- [ ] **Mock up** before coding. The perf log already has phases with durations, per-source cost and
      index build time. The page should show where the time went (phase bar), sources ranked by
      cost, then the raw report last. Today it is one stat tile and a collapsed expander.
- [ ] Build it from the approved mockup.

## 7. Automation / accessibility hang

- [ ] **Walking the automation tree over a loaded grid pegs the UI thread for minutes.** Affects
      Narrator and any accessibility tool, not just test harnesses. Needs a virtualized automation
      peer for the grid rows (or peers that don't realize every row). Scope as its own piece of work.
- [ ] **Switching the filter dock Left → Top at runtime breaks the UI Automation tree.** The screen is
      right, but UIA enumeration of the window stops at the first Time chip (63 nodes instead of ~340):
      the re-parented sections' cached peers still point at the old parent. Starting in Top mode is
      fine, and Top → Left is fine. Raising StructureChanged / InvalidatePeer on the containers did not
      help (Borders and StackPanels have no peers). Narrator would lose the band the same way. Fix
      candidates: give each moved section a peered root (a ContentControl), or rebuild the sections
      instead of moving them. The FlaUI dock test starts in Top mode to sidestep it.

## 8. UI tests in CI

- [x] **FlaUI smoke job.** `.github/workflows/ui-smoke.yml` runs the `UiSmoke`-tagged classes (8 classes,
      generated data, no LargeSamples) on windows-latest, non-blocking, with a desktop screenshot, the
      app's logs and a step summary as artifacts. Gate on it once it has been green for a while.
- [x] **More FlaUI coverage of the redesign** (`RedesignSmokeUITests`): Home Tools row (no
      "Shortcuts", no card action repeated as a tile), breadcrumb Home root + workspace chip counts +
      name-only title, filter dock (Top is compact with the Quick rules overflow; Left has the rail),
      in-row details with the Filter in / Filter out / Follow / Tag / Copy bar and no RawLevel row for a
      text log, and cancelling from the loading screen (Stop in the strip while running, stays Home,
      "cancelled" in Last run).
- [x] **Workspace flows** (`WorkspaceFlowUITests`, driven through the app's own MCP server, asserted
      on the window): load a file, add a second (chip 2 sources, pager 400), clear (chip 0, no
      locations), reload; open with a rule file (chip counts it, Rule filter toggle enabled, on = 40 of
      200 rows, off = 200, remove rules = chip 0); save / clear / reopen a workspace (name on the chip,
      sources and rows back); reopen a Recent search from Home after a clear (rows, chip, "from
      cache"); the Sources button lists both loaded files.
- Not doing a Deskhand capture / OCR layer (owner decision 2026-09-13): FlaUI tests are the lane.

## 9. Power-user review (2026-09-13)

A second review pass against the trace-expert persona (symptom → pivot → neighbourhood → pivot back,
keyboard-first, 5M-row logs). Owner triage: **2 and 3 approved**; 1 (a query history stack with
Alt+Left/Right and pivot pills) not adopted for now; the rest recorded as candidates.

### Approved

- [ ] **Time neighbourhood.** "Show me ±5 s around this row" does not exist in the viewer (MCP has
      `get_context`, the UI has nothing), and the relative chips (15m…7d) anchor to the data's max time,
      so they are useless mid-trace. Do: an "Around ▾" action in the row bar and right-click (±1 s /
      ±10 s / ±1 min / custom) that writes a `time >= … AND time <= …` predicate; a Ctrl+G "go to time"
      box taking an absolute timestamp or `+30s` relative to the selected row; query sugar
      `time ~ 12:34:56 ±2s` compiled to the same range. Effort M.
- [ ] **Query language reach and discoverability.** No completion while typing, parse errors only after
      Enter (`SearchQueryError`), help behind "?". Missing operators the persona uses daily: regex
      (`=~`; only `~` contains exists), `tag == Important` (tags are not a field), structured payload
      fields (`data.<key>`, json_extract in SQLite / dictionary lookup in memory). Do (a) field and
      operator completion in the search box, (b) inline parse error as you type, (c) the three new
      fields. (a)+(b) are S; (c) is the L part.

### Candidates (not scheduled)

- [ ] **Multiple sources are indistinguishable.** Source column hidden by default
      (`DefaultColumnVisibility` "Source": false); the Sources dialog has no per-file counts. Auto-show
      Source when more than one location is loaded; per-source counts in the dialog and the chip menu;
      a Source facet row like Level. S.
- [ ] **Keyboard reach for row actions.** Filter in/out, Follow, Tag, Copy are buttons and a context
      menu only. With the grid focused: I/O filter in/out on the focused column, F follow, T tag,
      Ctrl+C copy row, Enter toggle detail; list in "?" help; row actions in Ctrl+K when a row is
      selected. S/M.
- [ ] **Tags die with the session.** `_rowTags` is in-memory; exports write visible columns only; Copy as
      JSON/CSV omits tags. Persist tags per source signature (size+mtime) under LocalAppData; Tag
      column in exports when any tag exists; "Export tagged rows…" as a Markdown timeline. M.
- [ ] **Paging hides the timeline shape.** Page size 100 over 5M rows = 50k pages; "# go to" takes a
      page number; the histogram strip is decoration. Histogram click/drag → time predicate; go-to
      accepts `@12:34:56` and lands on the page containing that time. M.
- [ ] **Dense preset.** Index column off, Level as a 3-letter badge in the Time gutter, shorter time
      format (`HH:mm:ss.fff`), row font 11; selectable from Columns ▾, default above 100k rows. S.
- [ ] **Repeatable triage.** Workspaces save locations + rules, not the viewer state (query, sort,
      columns, tags, dock). Save view state in the workspace JSON; `save_view` / `apply_view` MCP tools;
      "Copy as MCP script" in Copy ▾. M.
- [ ] **Rule filter semantics are invisible.** Rules load with the toggle off and every row shows;
      nothing says so. An Active-filters pill "Rules: off (1 file) · apply" and a line in the loading
      summary. S.
- [ ] **Level chips and the query are two vocabularies** that silently AND together. Make the chips
      emit query predicates and render from the query. M.
- [ ] **UTC vs local is never stated.** Time renders without a zone marker; ETW is UTC, EVTX local. A
      zone toggle in Columns ▾ (Local / UTC / source), a suffix in the Time header, `time` predicates
      parsed in the same zone. S.

### Keep as is
Query AST compiling to both SQL and in-memory; Follow with only the axes a row has; the Run/Stop
strip and the stay-put cancel; the MCP `run_search → wait_for_load → get_page/get_context/summary`
story (keep names stable); streaming open with the stop-loading banner.

## Parked (not UX, tracked so they are not lost)

- Multi-process: activation batching → `DecodeScope` as `AsyncLocal` → cache / perf-log / MCP-port
  isolation. Decision made: single-instance today, multi-process long term.
- "Pick from values" for added field filters (facet code is per-field for the original four).
- Nine dialogs reviewed in code only, never captured on screen (rename workspace, cache-reuse, Kusto /
  Event Log source, colour pickers, Map CSV columns, triage, Customize toolbar, Tag note, Export complete).
