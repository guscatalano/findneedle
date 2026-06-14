# Testing Audit & Plan — Rules + UX

Audit date: 2026-05-25. Scope: `FindNeedleRuleDSL` and `FindNeedleUX` (plus their CI wiring).
Style targets: behaviour/integration, edge cases, snapshot/regression, UI automation.

## Progress

| Item | Status | Notes |
|---|---|---|
| R-01..R-04, R-12 | ✓ Done | `FindNeedleRuleDSLTests/RobustnessTests.cs` — 10 tests. |
| R-02 source-code fix | ✓ Done | Added 100 ms `Regex.IsMatch` timeout in `UnifiedRuleProcessor.cs`. Test caught real DoS surface; minimum fix applied. |
| R-05..R-08, R-10 | ✓ Done | `FindNeedleRuleDSLTests/OutputRuleProcessorTests.cs` — 13 tests (incl. R-11/Txt bonus). |
| R-13 snapshot tests | ✓ Done | `FindNeedleRuleDSLTests/ExampleSnapshotTests.cs` + 13 goldens under `TestData/snapshots/`. Hand-rolled. Hits all 13 example rule files × 3 providers (`*`, `EventLog`, `ETW`). Surfaces R-11 (empty providers = no match) and R-14 (`tag` vs `value` mismatch) — both currently pinned as-is. |
| Rule DSL suite | ✓ Green | 114/114 (was 78). |
| CI fail-on-red (CI #2) | ✓ Done | Added "Fail job if any tests failed" step at end of `test-publish` (parses TRX, exits 1 on any `Failed` outcome). Plugged release-on-red hole: `actually_release` now also requires `needs.test-publish.result == 'success'` (`create-release` was already gated). YAML re-validated. Badges still upload before the gate so they're visible even on red. |
| U-A1 / U-B1..U-B4 (SearchRules) | ✓ Done | Extracted `SearchRulesPageViewModel` + `IQueryStateService`/`MiddleLayerQueryStateService` seam; `SearchRulesPage.xaml.cs` now a thin proxy (282→~36 lines). `FindNeedleUXTests/ViewModels/SearchRulesPageViewModelTests.cs` — 9 tests. |
| U-A / U-B5 (SearchLocations) | ✓ Done | Extracted `SearchLocationsViewModel` + `ILocationStateService`/`MiddleLayerLocationStateService` seam; `SearchLocationsPage.xaml.cs` now a thin proxy, repeater bound once to a VM collection refreshed in place. `FindNeedleUXTests/ViewModels/SearchLocationsViewModelTests.cs` — 6 tests. |
| U-A3 / U-B6 (RunSearch) | ✓ Done | Extracted `SearchOrchestrator` + `ISearchRunner`/`MiddleLayerSearchRunner` seam; `RunSearchPage.xaml.cs` run/cancel logic now delegates (UI side effects via callbacks). `SearchOrchestratorTests` — 5 tests incl. prompt-cancel (≤200 ms), grace-window auto-open, cache/scanned status. |
| U-A4 / U-B7 / U-B8 (ViewerSettings) | ✓ Done | Extracted pure `NormalizeThreshold` + `ClampDetailsPanelHeight` helpers (U-B8) and added an internal storage-path seam (`SetStorageLocationForTests`/`ReloadFromDiskForTests`/`ResetStorageForTests`, exposed via `InternalsVisibleTo`) so the file round-trip (U-B7) can be tested against a temp file without touching the real `viewer-settings.json`. `ResultsViewerSettingsTests` — 11 tests (`[DoNotParallelize]`). |
| U-D (web viewer specs) | ✓ Done | Made `ResultsWebPage.BuildFilterSpec`/`BuildSortSpec` (pure DataTables-request → `FilterSpec`/`SortSpec` translators) `internal` and tested them — column-index mapping, Level `^…$` anchor stripping, time-range parse, sort dir/None/unknown-column. `ResultsWebPageSpecTests` — 8 tests. (U-D1 per-row JSON golden skipped: reflection field ordering would make the snapshot flaky.) |
| UX VM suite | ✓ Green | 39/39 ViewModel-category tests. |
| Open-log → results E2E | ✓ Done | `LogLoadEndToEndTests` (TestCategory `Integration`) — drives a full `NuSearchQuery.RunThrough()` over a real on-disk `.log` parsed by the real `PlainTextProcessor`, then reads results back from `ResultStorage` like the viewer does (raw count, filtered/viewable store, parsed Level/Message/timestamp). Covers the "open a log, see results" path **without** FlaUI (CI-runnable) — the integration-level answer to U-C4. 2 tests. |
| Virtualized rows (paged source) | ✓ Done | `PagedRowsEndToEndTests` — runs a real SQLite-backed search, then drives the **`IPagedLogSource`** layer the viewer actually reads through (`PagedLogSourceFactory.Create` → `SqlitePagedSource`): page windows (offset/limit), partial last page, past-the-end empty, no cross-page overlap, total/filtered counts, monotonic time sort, and level filtering. This is the virtualized-row paging logic the plain E2E test skipped. 1 test. |
| Row scroll / load speed | ✓ Done | `RowScrollingPerformanceTests` (TestCategory `Performance`, local-only) — populates 50k rows in SQLite and times `GetPage` across initial load, a sequential top-down scroll, and deep jumps (the OFFSET-scan worst case). Measured: first page ~8 ms, sequential pages median ~0.5 ms, scroll-to-bottom (offset 49,900) ~2 ms — paging is not a bottleneck; only the initial populate is disk-bound. Asserts correctness + a generous per-page ceiling (won't flake), real numbers in console output. Bumping `RowCount` to 500k shows shallow scroll stays flat (~0.5 ms) while deep-jump grows with OFFSET (~19 ms) — expected for SQLite `OFFSET`. |
| ETL inspector engine | ✓ Done (engine) | `EtlInspector.Inspect(path)` → `EtlInfo`: Windows/OS build, pointer size, CPU count, capture start/end + duration, event count + lost, kernel-vs-manifest/TraceLogging format breakdown, and providers with per-provider counts (via TraceEvent — no tracefmt). `EtlInspector.Format()` renders a report. Tested by `InspectEtl_ReportsBuildProvidersAndCounts` against a generated .etl. This is the backend for an "Inspect ETL" UI button (button not yet wired). |
| ETL: TraceLogging + manifest E2E | ✓ Done | `GeneratedEtlEndToEndTests` generates both a manifest `EventSource` and a self-describing **TraceLogging** `.etl`, then runs each through the full pipeline via the TraceEvent fallback: manifest 2003 rows, TraceLogging 2000 rows. |
| ETL: modern-trace decode + E2E | ✓ Done | `ETLProcessor` only decoded WPP traces (via `tracefmt`); a modern `.etl` (EventSource/manifest/kernel) came through as all-"Unknown" → 0 rows. Added a **TraceEvent fallback**: when tracefmt yields nothing for a real `.etl`, decode it directly with `ETWTraceEventSource` (`Dynamic.All` + `Kernel.All`), reusing the existing `ETLLogLine(TraceEvent)` ctor. New `GeneratedEtlEndToEndTests` (Performance, local-only) generates a real `.etl` via a file-mode `TraceEventSession`+custom `EventSource`, then runs it through `FolderLocation`+`ETLProcessor`→`NuSearchQuery`→storage: **2003 rows loaded** (was 0). WPP path unchanged — `ParseSimple` (real `test.etl`) still passes. |
| Open-log populate cost | ✓ Done | `LogPopulatePerformanceTests` (TestCategory `Performance`, local-only) — times opening a generated 100k-line log, split into parse-only / full→InMemory / full→SQLite. Finding: parsing ~45 ms (2.2M rows/s), RAM store ~210 ms, but the **on-disk SQLite + FTS store dominates** (~22 s here, ~99% of populate) — the initial-load cost is disk-write + FTS-index bound, not parsing or row handling. Mitigated in practice by the InMemory tier for small logs and cache-reuse on reopen. |
| Perf-test gating + CI | ✓ Fixed | (1) `ComparativeWritePerformance_2MillionRecords` self-skip was doubly broken: RAM detection used `GC.GetTotalMemory` (managed heap, ~0 GB → "needs 4 GB" never met) and `CheckSystemRequirements` bailed on `TestName` parsing (bare method name → `Length < 2`), so `[RequiresMinimumSpecs]` never ran and the 2M-record stress test ran unconditionally and timed out on slow hardware. Fixed both (real RAM via `GC.GetGCMemoryInfo().TotalAvailableMemoryBytes`; resolve class via `FullyQualifiedTestClassName` + attribute fallback) — verified RAM now reports 61.81 GB. (2) Excluded `TestCategory=Performance` from the CI gate filter so perf tests stay local-only. The `Performance_InsertOneMillion` (Sqlite/Hybrid ~2 min) tests pass but were needlessly slowing CI. (3) Made the comparative test's scale per-storage (renamed `ComparativeWritePerformance_LargeDataset`): in-memory-backed tiers write 2,000,000 (prove they scale), direct SQLite writes 500,000 so it finishes its 80s budget on a slow disk. Now PASSES all four on this hardware — InMemory 2M/0.99ms, Hybrid 2M/1.22ms, HybridCapped 2M/57.6ms (~23s), Sqlite 500k/404ms (~40s). The historical 2M numbers below were measured before this change. |
| Storage GetStatistics perf | ✓ Fixed | The 2M-record perf report showed HybridCapped spending ~93s in `GetStatistics` (40 calls). Three distinct O(rows)/contention bugs found & fixed: (1) `SqliteStorage.GetStatistics` ran two `SELECT COUNT(*)` full-table scans per call → now maintains O(1) running counts (`_rawCount`/`_filteredCount`) updated on insert/clear, seeded once at construction. (2) `InMemoryStorage.GetStatistics` recomputed `sizeInMemory` by iterating every record + UTF8-byte-counting all fields per call → now a running total maintained on add/remove/clear. (3) `SqliteStorage.GetStatistics` took `_sync`, blocking behind an in-progress write/spill on a slow disk (the bulk of the ~93s was lock-wait) → counts made `volatile`, GetStatistics is now lock-free. Result: Sqlite 0.54s→0.00s, Hybrid 3.07s→0.00s, HybridCapped 92.91s→0.01s; HybridCapped now completes 2M writes in ~24s and PASSES (was TIMEOUT). Added `GetStatistics_AfterReopen_ReflectsPersistedCounts`. CoreTests 130/130 (ex-Performance). |
| Storage cache-reuse bug | ✓ Fixed | Full-suite run surfaced 5 red `StorageTests` (Sqlite/Hybrid reopen → 0 rows). Root cause: `SqliteStorage` constructor still called `ClearTables()` unconditionally, defeating the documented cache-reuse contract (and the warm-cache feature was dead in production — a `cache.hit` could never fire). Fix: constructor opens without wiping; clearing moved to the consumer (`NuSearchQuery.Step2` clears before a fresh scan; `EvaluateCacheReuse` clears on a miss). Added `ISearchStorage.ClearTables()` + impls + a no-stale-duplicates regression test. CoreTests 132/132. NOTE: this had to be fixed because the new CI fail-on-red gate would otherwise turn the build red on these pre-existing failures. |

Next: remaining page VM extractions as needed. **Decided against CI #1** (a UI-tests job in CI): per maintainer decision the FlaUI UI tests are run **manually**, not on CI — so they stay `[TestCategory("UITests")]` and excluded from the CI test filter. (Tier U-C FlaUI workflow tests below remain optional/manual for the same reason.)

---

## 1. Coverage today

### Rules — `FindNeedleRuleDSL*` (78 tests, 5 files)

| File | Tests | What it covers |
|---|---|---|
| `UnifiedRuleProcessorTests.cs` | 10 | match / unmatch / disabled / provider filter / case-insensitivity / multi-rule |
| `FindNeedleRuleDSLPluginTests.cs` | 17 | plugin façade: load file, ProcessResults, tag counting, friendly name |
| `SystemConfigTests.cs` | 22 | `systemConfig` shape: plugins, search, tools, storage type |
| `DeprecatedPluginReplacementTests.cs` | 18 | RuleDSL replacing legacy filter/processor/output behaviour |
| `SampleLogRulesIntegrationTests.cs` | 11 | end-to-end against `sample-errors.log` |

### UX — `FindNeedleUX*` (18 tests, 2 files)

| File | Tests | What it covers |
|---|---|---|
| `FindNeedleUXTests/SearchRulesPageLogicTests.cs` | 12 | `RuleFileItem` / `RuleSectionItem` view-objects + ObservableCollection events |
| `FindNeedleUX.UITests/SearchRulesPageUITests.cs` | 6 | FlaUI smoke: 4 elements exist on `SearchRulesPage`, 2 are `[Ignore]`d |

### CI today (`.github/workflows/dotnet-desktop.yml`)

- Discovers any `*Tests/*.csproj` and runs each with `dotnet test --filter "TestCategory!=SkipCI&TestCategory!=UITests"`.
- TRX + Cobertura coverage uploaded, three badges generated, `fail_below_min: false` (coverage is informational).
- **Gap:** UI tests carry `[TestCategory("UITests")]` and `[TestCategory("SkipCI")]` → they never run on PRs. The badge says "tests pass" while UI assertions are skipped.

---

## 2. Rules — gap analysis

Source under test: `UnifiedRuleProcessor.cs`, `OutputRuleProcessor.cs`, `FindNeedleRuleDSLPlugin.cs`, `UnifiedRuleModel.cs`, and the search-pipeline integration in `FindPluginCore/Searching/RuleDSL/`.

### What's missing

**A. Malformed input — the regex fallback path is silent.** `UnifiedRuleProcessor.Process` (lines 33–42) catches every regex exception and silently falls back to substring. There is no test that proves the fallback fires, no test that an invalid regex still produces a defined result, and no test for catastrophic-backtracking inputs (e.g., `^(a+)+$` against `aaaaaaaaab`). Today an evil rule file can hang the search thread.

**B. `OutputRuleProcessor` has zero direct unit tests.** It's 788 lines: CSV/JSON/XML/TXT writers, `{date}/{time}/{output}` path expansion, XML name sanitisation, CSV escaping, the UML branch with 6+ path-resolution strategies, tag-based result filtering by reflection. All of this is currently exercised only incidentally via the integration suite.

**C. No schema-version negotiation tests.** `UnifiedRuleSet.SchemaVersion` exists but no test asserts behaviour when it's missing, wrong, or future-versioned.

**D. No round-trip snapshot tests.** 13 example `.rules.json` files exist under `FindNeedleRuleDSL/Examples/` — none of them are run as fixtures with locked-in expected output. A refactor today can silently change the meaning of `crash-detection.rules.json` and no test will notice.

**E. `tag` action coverage drift.** `FindNeedleRuleDSLPlugin.ProcessResults` reads `action.Tag`, but `OutputRuleProcessor.ProcessSection` reads `ruleObj.tag` (top-level) for output filtering. The two reads can't possibly agree on a fixture — there's no test that pins this convention.

**F. No pipeline-ordering tests.** `RuleDSLSearchPipeline` runs filter → enrichment → output. There is no test that an enrichment rule's tags are visible to a downstream output rule's `tag:` filter in the same pass.

**G. No concurrency tests.** `TagStore.cs` exists but isn't covered by the test list above. If filter rules run on multiple locations in parallel (they do — Step2 scans locations concurrently), tag writes are a race.

### Prioritized test cases

| ID | Pri | Surface | Test |
|---|---|---|---|
| R-01 | P0 | `UnifiedRuleProcessor` | Invalid regex (`[unclosed`) → fallback to substring → returns the substring match, doesn't throw. |
| R-02 | P0 | `UnifiedRuleProcessor` | Pathological regex (`(a+)+$`) on long non-matching input completes under 1 second (catches backtracking). Mark as perf budget test. |
| R-03 | P0 | `FindNeedleRuleDSLPlugin` | Rules file with malformed JSON → `ProcessResults` does not throw, returns 0 tags, logs to `Debug.WriteLine`. Capture trace via `Trace.Listeners`. |
| R-04 | P0 | `FindNeedleRuleDSLPlugin` | Rules file path doesn't exist → no throw, 0 matches. |
| R-05 | P0 | `OutputRuleProcessor` | CSV: result with comma, quote, newline in `Message` is properly escaped + round-trips through CsvHelper or `string.Split` after unescaping. |
| R-06 | P0 | `OutputRuleProcessor` | JSON output is valid JSON (`JsonDocument.Parse`) for 0, 1, and 10k results. |
| R-07 | P0 | `OutputRuleProcessor` | XML output is valid XML; field name with `<`, `&`, digit-prefix is sanitised. |
| R-08 | P1 | `OutputRuleProcessor` | `{date}` / `{time}` / `{datetime}` / `{output}` / `{temp}` in `path` all expand correctly; `{date}` matches `\d{4}-\d{2}-\d{2}`. |
| R-09 | P1 | `OutputRuleProcessor` | UML action with no `rulesFile` falls back to bundled `crash-detection-uml.rules.json` without throwing. |
| R-10 | P1 | `OutputRuleProcessor` | `format: "unknown"` doesn't crash — logs and skips (already the behaviour; pin it). |
| R-11 | P1 | `UnifiedRuleProcessor` | Provider list is case-insensitive (already covered), AND providers `[]` matches nothing (pin behaviour). |
| R-12 | P1 | `UnifiedRuleProcessor` | `Unmatch` invalid regex falls back to substring (mirror of R-01 for unmatch). |
| R-13 | P1 | Integration | All 13 `Examples/*.rules.json` files: parse + run against a fixed 50-line log → snapshot the produced tag-counts dictionary. Locked in via `[DataRow]`-driven test + golden JSON files under `TestData/snapshots/`. |
| R-14 | P1 | Integration | `tag` action key normalisation: write a test that uses both `"tag"` and `"value"` on the action object (the model supports both — line 209/212 of `UnifiedRuleModel`); assert which one wins and document it. |
| R-15 | P2 | Integration | Filter → enrichment → output in one pass: a result tagged by enrichment is then picked up by an output rule that filters by `tag:`. Currently undefined — pin it. |
| R-16 | P2 | `TagStore` | Concurrent `Add` from 8 threads of 1000 tags each → no exception, all 8000 present. |
| R-17 | P2 | Schema | Missing `schemaVersion`, `schemaVersion: "999.0"` → behaviour is graceful (warn or proceed with defaults). |
| R-18 | P2 | Property-based | FsCheck or hand-rolled: random rule sets of N=100 rules produce a stable output for the same input (idempotence). |

**Files to create:**
- `FindNeedleRuleDSLTests/OutputRuleProcessorTests.cs` — R-05..R-11
- `FindNeedleRuleDSLTests/RobustnessTests.cs` — R-01..R-04, R-12, R-17
- `FindNeedleRuleDSLTests/ExampleSnapshotTests.cs` — R-13
- `FindNeedleRuleDSLTests/TestData/snapshots/*.json` — golden outputs
- `FindNeedleRuleDSLTests/TagStoreTests.cs` — R-16 (currently zero tests)

---

## 3. UX — gap analysis

19 of ~20 pages have no test coverage. The one that's tested (`SearchRulesPage`) has 12 view-object tests + 6 FlaUI element-exists smoke tests; none of them actually drive a workflow.

### Pages and their testable surfaces

| Page | Untested? | Testable workflows |
|---|---|---|
| `WelcomePage` | Y | Recent files list renders, click-to-open. |
| `SearchLocationsPage` | Y | Add folder/file/event-log location, remove, set depth, persist to query. |
| `SearchRulesPage` | partial | Add rule file, validation banner on bad JSON, removal, purpose filter combo (currently only element existence). |
| `QuickLogWithRulesPage` | Y | One-shot: pick log + rule file → run → results visible. |
| `RunSearchPage` | Y | Cancel button cancels search, progress reporter updates, error surfaces. |
| `SearchResultPage` (web) | Y | Below/above 10k threshold render mode switch (`ResultsViewerSettings.WebViewerServerSideThreshold`). |
| `ResultsWebPage` | Y | DataTables pagination, level-color theme push, server-side `getPage` round-trip. |
| `NativeResultsPage` | Y | Column sort, filter, AutoHide column behaviour, paging. |
| `ResultsViewerSettingsPage` | Y | Settings save + reload, threshold field validation. |
| `PluginsPage` | Y | Plugin list reflects `PluginConfig.json`, enable/disable toggle persists. |
| `SearchProcessorsPage` | Y | (legacy — confirm it still loads). |
| `DiagramToolsPage` | Y | PlantUML / Mermaid path browse, "test tool" success/failure messages. |
| `SystemInfoPage` | Y | Renders without exception on a fresh machine. |
| `ProcessorOutputPage` | Y | Output file path opens in file-explorer. |
| `LogsPage` | Y | Tail-follow on `findneedle_log.txt`. |

### Why the existing UX tests aren't enough

- `SearchRulesPageLogicTests.cs` tests `ObservableCollection<T>.Add` — that's testing .NET, not the page. The actual logic in `SearchRulesPage.xaml.cs` (`LoadRuleFile`, `SyncRulesToQuery`, the purpose filter combo, the validation status) is **not** under test.
- `SearchRulesPageUITests.cs` only verifies four elements exist. It doesn't click Browse, doesn't load a file, doesn't assert the validation badge appears.
- `[TestCategory("SkipCI")]` means CI never catches a UX regression.

### Prioritized test cases

**Tier U-A: Extract view-models so logic is testable without a window.**

Today most page logic lives in `*.xaml.cs` code-behind, which can't be instantiated without the WinUI runtime. Step zero is a small refactor: pull testable logic into plain classes the unit-test project can `new` up.

| ID | Pri | Page | Refactor |
|---|---|---|---|
| U-A1 | P0 | `SearchRulesPage` | Extract `LoadRuleFile`, validation, and `SyncRulesToQuery` into `SearchRulesPageViewModel`. |
| U-A2 | P0 | `SearchLocationsPage` | Extract location list management (add/remove/persist). |
| U-A3 | P0 | `RunSearchPage` | Extract `SearchOrchestrator` covering the cancel + progress wiring. |
| U-A4 | P1 | `ResultsViewerSettingsPage` | Extract settings serialisation. |

**Tier U-B: View-model unit tests (no UI thread needed).**

| ID | Pri | Test |
|---|---|---|
| U-B1 | P0 | `SearchRulesPageViewModel.LoadRuleFile` with valid file → 1 valid item, sections parsed. |
| U-B2 | P0 | Same with malformed JSON → 1 invalid item, `ValidationError` populated. |
| U-B3 | P0 | Same with file-not-found → invalid + correct error. |
| U-B4 | P0 | `LoadRulesFromQuery` does not re-enter (the `_isLoadingFromQuery` guard) when query change fires mid-load. |
| U-B5 | P0 | `SearchLocationsViewModel.Add` / `Remove` persists to `MiddleLayerService.GetCurrentQuery()`. |
| U-B6 | P0 | `SearchOrchestrator.Run` cancels promptly when `CancellationTokenSource.Cancel()` is invoked (≤ 200 ms). |
| U-B7 | P1 | `ResultsViewerSettings` round-trip: write → read → identical instance. |
| U-B8 | P1 | `ResultsViewerSettings.WebViewerServerSideThreshold` rejects negative values. |

**Tier U-C: FlaUI driven workflows (currently `SkipCI` — must run on CI).**

Each test launches the built `FindNeedleUX.exe`, drives it, asserts a visible outcome. These are slow (~5–10 s each) so keep them small and grouped per page.

| ID | Pri | Workflow |
|---|---|---|
| U-C1 | P0 | Smoke: app starts, MainWindow appears, no unhandled exception in 5s. |
| U-C2 | P0 | `SearchRulesPage`: Browse → file picker → cancel → 0 files in list. (Use `Windows.Storage.Pickers` automation, or call `AddRuleFileByPath` via UIA pattern.) |
| U-C3 | P0 | `SearchRulesPage`: load malformed rules file → red-status badge visible, item count = 1. |
| U-C4 | P0 | `QuickLogWithRulesPage`: pick log + rule file → Run → results page appears with ≥1 row. (Use canned `Samples/`-like fixture.) |
| U-C5 | P1 | `RunSearchPage`: start search → click Stop within 500 ms → search task cancels, status reads cancelled. |
| U-C6 | P1 | `NativeResultsPage`: open with a 5k-row SQLite-backed source → sort header click changes order. |
| U-C7 | P1 | `ResultsWebPage`: open with 50 rows (under threshold) → client-side mode, DataTable renders. Then with 20k rows (above threshold) → server-side mode, paging `getPage` is exercised. |
| U-C8 | P2 | `ResultsViewerSettingsPage`: change theme → results viewer reflects new colours on next open. |
| U-C9 | P2 | `PluginsPage`: toggle a plugin → `PluginConfig.json` on disk updates. |
| U-C10 | P2 | Navigation matrix: open every page, assert no XAML parse / binding exception in the debug log. Drives the 19 untested pages cheaply. |

**Tier U-D: Snapshot tests for HTML / data-binding output.**

`ResultsWebPage` injects data into a webview. Snapshot the generated JSON envelope + initial HTML so client-side template churn is visible.

| ID | Pri | Test |
|---|---|---|
| U-D1 | P1 | `ResultsWebPage.BuildClientSideJson(50 rows)` → matches golden file. |
| U-D2 | P1 | `ResultsWebPage.HandleGetPage(filterSpec, sortSpec, page=0, len=25)` → matches golden envelope. |

**Files to create:**
- `FindNeedleUX/ViewModels/` — view-models extracted from code-behind (Tier U-A).
- `FindNeedleUXTests/ViewModels/*.cs` — Tier U-B tests.
- `FindNeedleUX.UITests/Workflows/*.cs` — Tier U-C tests, each per page.
- `FindNeedleUX.UITests/TestData/` — canned logs + rules fixtures.

---

## 4. CI changes required

### Must-do

1. ~~**Run UI tests on PR.**~~ **Not doing this** (maintainer decision, 2026-06): the FlaUI UI tests are run manually rather than on CI. A draft `run-ui-tests` job was prototyped (closed PR #2) and dropped. The UI tests stay `[TestCategory("UITests")]` and excluded from the CI test filter; no branch-protection check for them.
2. **Fail the workflow on red tests.** `test-publish` runs with `if: always()` but does **not** fail when `run-tests` outcome ≠ success. Add a final step that exits non-zero when any TRX has `outcome="Failed"`. (The current setup publishes badges even on red.)
3. **Coverage threshold.** `fail_below_min: false` today. Once the new tests land, set thresholds to current numbers + 5 % and ratchet up.

### Nice-to-do

4. **Test categorisation.** Add `[TestCategory("Robustness")]`, `[TestCategory("Snapshot")]`, `[TestCategory("Workflow")]` so the matrix can shard and the badges can be split.
5. **Snapshot diff visibility.** When a snapshot test fails, post a `gh pr comment` with the diff. Cheap with `actions/github-script`.

---

## 5. Recommended order

If you implement bottom-up, each step pays for the next:

1. **R-01 .. R-04 + R-12** (rules robustness — ~1 day). Tiny tests against the regex fallback path that currently has zero coverage and a real DoS risk.
2. **CI change #1 + #2** (~½ day). Land UI tests on PR and make red tests block. Without this the new tests don't earn their keep.
3. **R-05 .. R-11** (`OutputRuleProcessor` — ~1 day). 788 lines with no direct tests is the single biggest rules gap.
4. **U-A1 + U-B1..U-B4** (`SearchRulesPageViewModel` extraction + tests — ~1 day). Establishes the view-model pattern.
5. **U-C1 + U-C10** (smoke + navigation matrix — ~½ day). Cheap UI test that protects every page from regressions.
6. **R-13 + snapshot infra** (~½ day). Once snapshot helpers exist, U-D follows.
7. Iterate U-A/U-B/U-C per page in priority order.

Total P0 work: ~3.5 dev-days. P0+P1: ~7 dev-days.

---

## 6. Open questions

- Is there appetite to refactor code-behind into view-models? Most UX testing improvements depend on it.
- Self-hosted runner available for `[UITests]`, or stay on `windows-latest`?
- Snapshot library preference: hand-rolled (current style), `Verify.Xunit`, or `Snapshooter.MSTest`?
- Do you want property-based tests (R-18) — adds FsCheck dependency.
