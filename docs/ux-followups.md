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

## Parked (not UX, tracked so they are not lost)

- Multi-process: activation batching → `DecodeScope` as `AsyncLocal` → cache / perf-log / MCP-port
  isolation. Decision made: single-instance today, multi-process long term.
- "Pick from values" for added field filters (facet code is per-field for the original four).
- Nine dialogs reviewed in code only, never captured on screen (rename workspace, cache-reuse, Kusto /
  Event Log source, colour pickers, Map CSV columns, triage, Customize toolbar, Tag note, Export complete).
