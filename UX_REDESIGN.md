# FindNeedle UX Redesign (WinUI 3)

## Goal

Make the RuleDSL workflow the primary path. The plugin/SearchQuery flow stays
available but stops being the default new-user entry point.

## Current state (codebase, not aspiration)

- Shell: `MainWindow.xaml` uses a top `MenuBar` + a content `Frame`. No `NavigationView`.
- Entry pages:
  - `WelcomePage` — shown on startup. Three quick-action buttons + empty
    "Recent Pipelines" placeholder.
  - `QuickLogWithRulesPage` — fully wired pick-a-log + pick-a-rules-file + run.
    This is the only end-to-end RuleDSL flow that actually works today.
  - `RuleDSLHomePage` — visual mockup. All Click handlers (`OnSaveClicked`,
    `OnTestClicked`, `OnAddFilterClicked`, `OnAddEnrichmentClicked`,
    `OnAddUmlClicked`, `OnNewRuleClicked`) are empty bodies.
- Configuration pipeline (works, but split across pages):
  `SearchLocationsPage` → `SearchFiltersPage` → `SearchRulesPage`
  → `SearchProcessorsPage` → `RunSearchPage` → results viewer.
- Result viewers (3, switched via `GlobalSettings.DefaultResultViewer`):
  `ResultsWebPage`, `ResultsVCommunityPage`, `SearchResultPage`.
- UML: `DiagramToolsPage` exists.

## Issues with the current UI

1. **Two menus alias the same pages.** `RuleDSL > Locations/Filters/Enrichment`
   navigates to the exact same pages as `SearchQuery > Locations/Filters`.
   `Enrichment` literally opens `SearchFiltersPage`. New users have no way to
   tell the menus apart.
2. **`WelcomePage` quick actions don't match the real fast paths.** Its three
   buttons go to configuration pages (`RunSearchPage`, `RuleDSLHomePage`,
   `DiagramToolsPage`). The real one-click flows live in the top-bar
   `Quick` menu (`Open Log File`, `Open Folder`, `Open Log with Rules`).
3. **`RuleDSLHomePage` is decorative.** Save/Test/Add-Filter/Add-Enrichment/
   Add-UML are no-ops. Run Search navigates to results without running.
4. **No persistent state indicator.** Users cannot see "I have N locations,
   M rule files loaded, last run produced K results" without navigating.
5. **"Recent Pipelines" is always empty.** Empty-state placeholder that
   never becomes non-empty.

## Phase 1 (the only phase that should exist right now)

### 1. Collapse duplicate menus

Drop the `RuleDSL` MenuBarItem. Keep `SearchQuery` and add a `Rules` item
to it (navigates to `SearchRulesPage`). One configuration menu, no aliases.

### 2. Rewire `WelcomePage` quick actions

Map the three buttons to the actual fast paths in `MainWindow.MenuFlyoutItem_Click`:

- "📁 Open Log File"  → `QuickFileOpen()`
- "📂 Open Folder"    → `QuickFolderOpen()`
- "📝 Open Log with Rules" → navigate to `QuickLogWithRulesPage`

### 3. Fix or delete `RuleDSLHomePage`

Pick one:

- **Fix**: wire `OnRunSearchClicked` to `MiddleLayerService.RunSearch` (same
  call the working `RunSearchPage.Button_Click` makes), wire `OnSaveClicked`
  to `SaveWorkspace`, replace the empty Add-Filter/Enrichment/UML handlers
  with a single `TextBox` that edits the rule JSON directly. RuleDSL JSON
  is the single source of truth — show it.
- **Delete**: remove from `MainWindow` menu, remove from `WelcomePage` button
  target. Keep `QuickLogWithRulesPage` as the RuleDSL entry point.

### 4. Persistent state strip

Add a one-line strip at the top of the `Frame` content area in
`MainWindow.xaml`, bound to `MiddleLayerService`:

`Locations: 2 · Filters: 0 · Rules: 1 · Last run: 75 results (2 min ago)`

Three `TextBlock`s in a `Grid.Row="0"` above `contentFrame`, refreshed on
navigation. ~40 lines.

### 5. Remove dead UI

- "Recent Pipelines" section on `WelcomePage` (always empty) — delete or wire
  to `GlobalSettings`-backed MRU of saved workspace paths.

## Out of scope (do not ship in Phase 1)

- `NavigationView` migration (no user-visible value while pages still mirror
  the legacy flow).
- WebView2/Mermaid in-app UML preview.
- Drag-and-drop rule reordering.
- Undo/redo.
- Pipeline canvas / data-flow visualization.
- "Auto-convert old plugin configs to RuleDSL" (no migration path designed,
  no consumer asking for it).

## Build note

Build `FindNeedleUX.csproj` with **VS MSBuild** (`MSBuild.exe`), not
`dotnet build`. The WinUI 3 XAML compiler task is .NET Framework 4.7.2 and
the SDK shells to `XamlCompiler.exe` via `<Exec>` — XAML errors are
swallowed and surface only as `exited with code 1`. VS MSBuild loads the
task in-proc and surfaces real errors (e.g. `WMC0055: Cannot assign text
value 'Diagram' into property 'Symbol'`).
