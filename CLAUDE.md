# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

**FindNeedle** is a Windows log-search utility: a WinUI 3 (Windows App SDK) desktop app plus a
command-line tool, built on a plugin + declarative-rule pipeline. C# 12 / .NET 8, targets
`net8.0-windows10.0.19041.0`. Windows-only — it depends on WinUI 3, ETW, and the Windows Event Log.

> **`AGENTS.md` is the authoritative, in-depth reference** (architecture internals, storage tuning,
> viewer perf, troubleshooting). Read it before any non-trivial change to storage, the search
> pipeline, the result viewers, or RuleDSL. This file is the quick orientation; `AGENTS.md` is the detail.

## Commands

```powershell
# Build the whole solution
dotnet build findneedle.sln

# Run the GUI app / the CLI
dotnet run --project FindNeedleUX\FindNeedleUX.csproj      # WinUI 3 desktop app
dotnet run --project findneedle\findneedle.csproj          # command-line tool

# Tests (MSTest). Run everything:
dotnet test findneedle.sln

# One test project
dotnet test CoreTests\CoreTests.csproj

# A single test or a name substring (MSTest filter syntax)
dotnet test CoreTests\CoreTests.csproj --filter "FullyQualifiedName~MyTestMethodName"

# By category (categories include: Storage, Performance, Installation, UML)
dotnet test --filter "TestCategory=Storage"

# The WinUI app on its own (APPX1101 at the end is expected locally - the build output is still good)
dotnet build FindNeedleUX\FindNeedleUX.csproj -c Debug -p:Platform=x64

# The FlaUI suite that CI also runs (build x64 first; FINDNEEDLE_UITEST_APP names the exe under test)
dotnet build FindNeedleUX.UITests\FindNeedleUX.UITests.csproj -c Debug -p:Platform=x64
dotnet test FindNeedleUX.UITests\FindNeedleUX.UITests.csproj --no-build --filter "TestCategory=UiSmoke"
```

**`FindNeedleUXTests` binds `FindNeedleUX.dll` by `HintPath` into `bin\Debug\...\win-x64`,** not
`bin\x64`. After building the app with `-p:Platform=x64`, run a plain `dotnet build -c Debug` on it too,
or the unit tests compile against a stale dll and fail with `CS0117` on APIs you just added.

Tests use **MSTest** (`[TestClass]` / `[TestMethod]`), not xUnit/NUnit. Test projects pair with their
source project by name (`FindNeedleRuleDSL` → `FindNeedleRuleDSLTests`, etc.).

### Test gotchas

- CI (`.github/workflows/dotnet-desktop.yml`) builds x64 / `win-x64` /
  `net8.0-windows10.0.19041.0` and runs `dotnet test` with
  `--filter "TestCategory!=SkipCI&TestCategory!=UITests&TestCategory!=Performance"` — tag tests
  with those categories to control where they run.
- **`ETWPluginTests` is x64-only**: build with `-p:Platform=x64` and run `vstest.console` against
  `bin\x64\Debug\...`. A plain `dotnet build` produces AnyCPU output and vstest can silently run a
  stale dll.
- If a WinUI-dependent test build fails under `dotnet test` with **MSB4062** (PriGen task), build
  with Visual Studio's MSBuild and run `vstest.console` on the output instead.
  (`FindNeedleUXTests` deliberately disables WinUI in its csproj so plain `dotnet test` works.)
- `FindNeedleUX.UITests` drives the real app with FlaUI (x64). The self-contained classes are tagged
  `UiSmoke` and run in CI on the hosted runner's desktop (`.github/workflows/ui-smoke.yml`, which gates:
  a red UI test fails the workflow); everything needing LargeSamples or the perf lane stays local. Build it
  with `-p:Platform=x64`; `FINDNEEDLE_UITEST_APP` names the exe under test explicitly.
- **UI tests must never synthesize real mouse or keyboard input on a dev machine** - a stray click or
  keystroke lands in whatever window has focus. Drive the app through UIA patterns (`Invoke`, `Toggle`,
  `Select`, `ExpandCollapse`) and scope popup searches to the app's own process windows; the few tests
  that really need typing are CI-only.
- UI tests isolate per-user state with `FINDNEEDLE_VIEWER_SETTINGS=<temp file>` (viewer settings, and
  `<that path>.home-sections.json` for the Home layout) and skip single-instancing with
  `FINDNEEDLE_NO_SINGLE_INSTANCE=1`. A test that wants the *real* single-instance behaviour (opening a
  second log) must leave that unset - and be inconclusive when a foreign FindNeedle is already running.
- Sample `*.log` test data is gitignored — a test that reads it passes locally but fails in CI
  unless the data file is tracked and copied to the test output.

## Architecture big picture

The system is built around three concepts. Read these as the mental model; details live in `AGENTS.md`.

**1. Plugins (legacy mechanism, still active for I/O).** Defined by interfaces in
`FindNeedlePluginLib/Interfaces/`. Three plugin kinds are first-class and should be kept:
- `ISearchLocation` — **data sources** (folders, ETW, Event Log, ZIP). They acquire raw data.
- `IFileExtensionProcessor` — **file-format parsers** (e.g. plain text).
- `ISymbolResolver` — **symbol locators**. Consulted (with a binary's PDB identity) when the built-in
  WPP symbol lookup can't find a PDB, so a plugin can fetch it from an SMB share / symbol server / REST
  service. Discovered like the others; hooked in `WppSymbolResolver.BuildTmfs`. Runs both on the manual
  "WPP Symbol Resolution" page **and on the decode path**: when a WPP `.etl` fails to decode for missing
  TMFs, `ETLProcessor` calls the `WppSymbolProvisioning` seam (`FindNeedlePluginLib`, an ambient static
  like `DecodeOptions`), which the UX registers to run `BuildTmfs` over the ETL's folder + configured
  symbol sources, then retries the decode once.

New I/O-acquisition seams like these are fine. The three **result-pipeline** plugin kinds
(`ISearchFilter`, `IResultProcessor`, `ISearchOutput`) are **deprecated** — their functionality moved to
RuleDSL. Don't add new ones of those; add RuleDSL rules instead.

**2. RuleDSL — the primary configuration system** (`FindNeedleRuleDSL/`). JSON files
(`*.rules.json`) declare filter / enrichment / UML / output rules so behavior changes without
recompiling. Core files: `UnifiedRuleModel.cs` (data model), `UnifiedRuleProcessor.cs` (evaluation),
`OutputRuleProcessor.cs` (output). Integrated into search via `FindPluginCore/Searching/RuleDSL/`.
A `SearchQuery`/`NuSearchQuery` pulls rules in through its `RulesConfigPaths` property and applies
them automatically in `RunThrough()`. See `FindNeedleRuleDSL/README.md`.

**3. The search pipeline** (`FindPluginCore/`, executed by `SearchQuery`/`NuSearchQuery.RunThrough()`):
load rules → Step1 load locations → Step2 scan + filter (and conditionally consolidate) → Step3
enrichment + processors → Step4 outputs → done. A key optimization: when there are no rules,
processors, or outputs (the "just view a log" case), Step2 **skips** re-materializing the full result
set — large searches stay lazy. The result viewers never hold the whole set in memory; they read
through `IPagedLogSource` (`FindNeedleUX/Services/PagedLogSource/`).

**Storage** has three backends (`InMemoryStorage`, `SqliteStorage`, `HybridStorage`) chosen
automatically by estimated row count (`<10k` in-memory, `10k–50k` hybrid, `>50k` SQLite). SQLite uses
an FTS5 trigram index for fast substring search. If you touch storage selection, perf tuning, or the
viewers, the relevant section of `AGENTS.md` documents the non-obvious tradeoffs (and the reasons
behind them) — consult it first.

## Project layout (orientation, not exhaustive)

- `findneedle/` — CLI executable; holds `PluginConfig.json` (legacy plugin config).
- `FindNeedleUX/` — WinUI 3 UI: `MainWindow`, `Pages/` (Search/Results/Rules/etc.),
  `ViewModels/`, `Services/`, `MiddleLayerService` (UI ↔ core bridge). `Services/Mcp/` hosts a
  built-in MCP server so an AI agent can drive the viewer and author rules.
- `FindPluginCore/` — search engine, `PluginManager`, storage, `Diagnostics/PerfLog`.
- `FindNeedlePluginLib/` — plugin interfaces and shared types.
- `FindNeedleCoreUtils/` / `FindNeedlePluginUtils/` — utilities (file I/O, storage helpers).
- `FindNeedleRuleDSL/` — rule engine. `FindNeedleUmlDsl/` — PlantUML/Mermaid diagram generation.
- `*Plugin/` (ETWPlugin, EventLogPlugin, ZipFilePlugin, BasicTextPlugin, CsvPlugin, JsonPlugin,
  PcapPlugin, Plugins/Kusto) — concrete plugins.

## The viewer (FindNeedleUX) — subsystems that span several files

**The search box is a small query language**, not a substring match. `FindPluginCore/Searching/Query/`
holds it: `LogQuery.TryParse` builds an AST that is compiled **twice** — to SQL (`AppendSql`, run by
`SqliteStorage`) and to an in-memory predicate (`Evaluate`, used by the other backends). Both paths must
agree, so a new field or operator means touching `ColumnOf`/`AppendSql` *and* `Evaluate`, plus a test in
`CoreTests/LogQueryPowerTests.cs`. Plain text with no operator still means "any column contains this".
`QueryEditor` is the programmatic side: the viewer's Filter/Follow/Around pivots call `AddClause`, which
is **additive and replaces same-axis clauses** (following a second process swaps the process clause, it
doesn't stack). `QuerySuggestions` feeds the AutoSuggestBox completion list.

**User-arrangeable UI lists follow one "catalog" pattern** (`FindNeedleUX/Services/*Catalog.cs`:
`StatusBarCatalog`, `QuickActionCatalog`, `HomeSectionCatalog`, `MessageReformatCatalog`).
Each is a static class with a shipped default list, a JSON file under `%LocalAppData%\FindNeedle`, a
`Changed` event the page re-applies on, and a `SetStorageLocationForTests` seam — so the arrangement logic
is unit-tested without the UI. When a catalog's defaults change, migrate old stored values
(`StatusBarCatalog.LegacyDefaults` is the worked example) rather than resetting the user's layout.

**Process model.** `Program.cs` runs before WinRT init and handles three modes: the `--mcp-stdio` bridge
(see below), single-instancing via `AppInstance.FindOrRegisterForKey("findneedle-main")` — a second
launch forwards its file to the running app — and the normal path. `FINDNEEDLE_NO_SINGLE_INSTANCE=1` or
`--no-single-instance` skips registration.

**MCP.** `Services/Mcp/McpServer` is an HTTP JSON-RPC server inside the app (off by default, port in
settings). Because a client that spawns a dead server errors on every call, clients instead run
`FindNeedleUX.exe --mcp-stdio` (`McpStdioBridge`), which speaks stdio, forwards to the HTTP endpoint and
launches/waits for the app when it isn't up. `docs/MCP_DESIGN.md` has the details.

**Cache schema.** Any change to the SQLite row layout must bump `SqliteStorage.CacheSchemaVersion` *and*
extend `EnsureColumns`, or cached searches from an older build deserialize into the wrong columns.

## Release mechanics

Pushing to `master` runs `.github/workflows/dotnet-desktop.yml`, which auto-tags the next patch version
(`1.0.N`) and commits the `Package.appxmanifest` version bump with `[skip ci]`. A **Store release** is a
`v1.0.N` tag: only `v*.*.*` tags run the `publish-store` job. Two traps: a tag pointing at a `[skip ci]`
commit triggers nothing (tag the real commit), and the `msstore` publish step fails transiently often
enough that re-running the job is the normal fix, not a signal something is wrong. `ui-smoke.yml` is a
separate job, and it gates too.

## Conventions worth knowing

- New filtering/enrichment/output behavior goes in **RuleDSL**, not new deprecated-interface plugins.
- Adding a RuleDSL field touches three files: `UnifiedRuleModel.cs`, `UnifiedRuleProcessor.cs`, and the
  rule-matching engine — keep them in sync.
- Diagnostics: app writes a structured timing log to `%LocalAppData%\FindNeedle\perf-log.txt`. When
  investigating "search/viewer is slow," read it — phase events (`search.run`, `consolidate.skipped`,
  `viewer.*.load`, etc.) point at where wall-clock time went.
- The app runs **unpackaged** in dev, so WinRT `ApplicationData.Current.LocalSettings` throws —
  persist UI settings to a file under `%LocalAppData%\FindNeedle` instead.
- `PcapPlugin` parses .pcap/.pcapng with a hand-written managed reader + PacketDotNet — deliberately
  not SharpPcap, to avoid native libpcap dependencies. Keep it managed-only.
- `.editorconfig` at the repo root governs formatting/style — follow it.
- Commit gate: the suite a change touches must print `Passed!` before committing, and each commit
  must build on its own — the pipeline tags and versions every push to `master`.
