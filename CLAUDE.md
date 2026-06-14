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
```

Tests use **MSTest** (`[TestClass]` / `[TestMethod]`), not xUnit/NUnit. Test projects pair with their
source project by name (`FindNeedleRuleDSL` → `FindNeedleRuleDSLTests`, etc.).

## Architecture big picture

The system is built around three concepts. Read these as the mental model; details live in `AGENTS.md`.

**1. Plugins (legacy mechanism, still active for I/O).** Defined by interfaces in
`FindNeedlePluginLib/Interfaces/`. Two plugin kinds are still first-class and should be kept:
- `ISearchLocation` — **data sources** (folders, ETW, Event Log, ZIP). They acquire raw data.
- `IFileExtensionProcessor` — **file-format parsers** (e.g. plain text).

Three other plugin kinds (`ISearchFilter`, `IResultProcessor`, `ISearchOutput`) are **deprecated** —
their functionality moved to RuleDSL. Don't add new ones; add RuleDSL rules instead.

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
  `ViewModels/`, `Services/`, `MiddleLayerService` (UI ↔ core bridge).
- `FindPluginCore/` — search engine, `PluginManager`, storage, `Diagnostics/PerfLog`.
- `FindNeedlePluginLib/` — plugin interfaces and shared types.
- `FindNeedleCoreUtils/` / `FindNeedlePluginUtils/` — utilities (file I/O, storage helpers).
- `FindNeedleRuleDSL/` — rule engine. `FindNeedleUmlDsl/` — PlantUML/Mermaid diagram generation.
- `*Plugin/` (ETWPlugin, EventLogPlugin, ZipFilePlugin, BasicTextPlugin, Plugins/Kusto) — concrete plugins.

## Conventions worth knowing

- New filtering/enrichment/output behavior goes in **RuleDSL**, not new deprecated-interface plugins.
- Adding a RuleDSL field touches three files: `UnifiedRuleModel.cs`, `UnifiedRuleProcessor.cs`, and the
  rule-matching engine — keep them in sync.
- Diagnostics: app writes a structured timing log to `%LocalAppData%\FindNeedle\perf-log.txt`. When
  investigating "search/viewer is slow," read it — phase events (`search.run`, `consolidate.skipped`,
  `viewer.*.load`, etc.) point at where wall-clock time went.
- `.editorconfig` at the repo root governs formatting/style — follow it.
