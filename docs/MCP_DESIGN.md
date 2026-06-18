# MCP Server Design — Live Viewer Control

Status: **design only, not yet implemented.**

## Goal

Expose FindNeedle's loaded log data to an MCP client (an agent) so it can both **read**
and **drive** what the user sees. The agent and the user share **one live workspace** —
the same locations, search query, and active results viewer. Agent actions show up in the
GUI immediately; the user's actions are visible to the agent on its next read.

## Decisions (settled)

- **In-app only.** The MCP endpoint is hosted inside `FindNeedleUX`. No standalone console host.
- **Drive, not just read.** The agent can do the same things a user can: add locations, run a
  search, set filters/sort/page, tag/select rows, switch details mode, export.
- **No "session" concept.** There is exactly one workspace — the app's current state. We do
  *not* model a separate MCP session (that was the awkward part; it didn't match GUI semantics).
- **Transport:** HTTP/SSE, bound to `127.0.0.1` only. **No token** (localhost is sufficient).
  **Off by default**, enabled via a viewer setting.
- **Stable row id:** surface the SQLite row `Id` on each record as the durable handle for
  `get_record` / `tag_row` / `select_row`. (Also makes row tagging robust — today it is keyed
  by row content.)
- **Scope:** full — query + rules/enrichment + export.

## Architecture

```
 agent ──HTTP/SSE (127.0.0.1, off by default)──▶ FindNeedleUX MCP endpoint
                                                       │ calls
                                                 McpViewerBridge  ──▶ MiddleLayerService (locations, run search)
                                                       │           └─▶ active NativeResultsPage / ViewModel
                                                       │               (filters, sort, page, tags, details mode)
                                                 marshals every mutation to the UI thread
```

- **MCP endpoint host** — wires the official `ModelContextProtocol` C# SDK over HTTP/SSE inside
  the WinUI app process. Lifetime tied to the app; started/stopped by the settings toggle.
- **`McpViewerBridge`** — a service that the active `NativeResultsPage`/ViewModel registers with
  when it loads (and deregisters on unload). MCP tools call the bridge. If no viewer is
  registered, viewer-dependent tools return a clear "no active view" error.
- **UI-thread marshaling** — MCP requests arrive on HTTP worker threads. Every mutating call
  hops to the UI thread via `DispatcherQueue.TryEnqueue` and awaits completion through a
  `TaskCompletionSource`, so the GUI updates live and the HTTP handler gets a real result.
  Pure data reads (counts/pages) can go straight to `SqliteStorage` (lock-safe).

## Tools

### Workspace (mirrors Search Locations page + the Search button)
- `list_locations`
- `add_folder(path)`
- `add_kusto(cluster, db, kql, auth, rowLimit)`
- `remove_location(name)`
- `list_rules`, `set_rules(paths)`
- `run_search()` / `cancel_search()` → returns a summary (count, time span, level breakdown)

### Drive the viewer (mirrors the toolbar / filter boxes / pager)
- `get_view()` → current filter + sort + page + pageSize + totalFiltered + total
- `set_filter(search?, provider?, taskName?, message?, source?, level?, from?, to?)` → new count
- `clear_filters()`
- `set_sort(column, desc)`
- `goto_page(n)`, `set_page_size(n)`
- `get_page()` → rows currently shown (compact form, capped)
- `get_record(id)` → full row by stable SQLite `Id`
- `summary()` → totals, per-level counts, time range, sources
- `histogram(bucket)` → counts bucketed by time/level (see shape without pulling rows)

### Drive + annotate
- `select_row(id)`
- `tag_row(id, tag)` / `clear_tag(id)`
- `set_details_mode(inrow | bottom | popup)`
- `export(format = csv|json|xml)` → writes current filtered set, returns `{ path, rowCount }`

### Token-budget rules (apply to all result-returning tools)
- Paginate everything; always return a total count alongside the page.
- Truncate long messages in list/`get_page` results; full text only via `get_record`.
- Compact record by default; a `fields` arg can request more.

## Record shape

Compact (list / `get_page`):
```json
{ "id": 12345, "time": "2026-01-25T06:32:35Z", "level": "Error",
  "provider": "X", "message": "…truncated to ~300 chars…" }
```
`get_record` adds full message + source/task/machine/user + the raw searchable line.

## Refactors this implies

1. **Stable id** — surface the SQLite row `Id` on results (small change; also fixes tagging).
2. **`McpViewerBridge`** service + viewer register/deregister on page load/unload.
3. **Headless-drivable hooks** on the ViewModel/page: filter/sort/page already exist as
   properties; add `TagRow`, `ClearTag`, `SelectRow`, and expose row `Id`.
4. **Export extraction** — move CSV/JSON/XML serialization out of `NativeResultsPageViewModel`
   into a shared headless exporter used by both the VM and MCP.
5. **MCP host wiring** — `ModelContextProtocol` SDK over HTTP/SSE + settings toggle in
   `ResultsViewerSettings` / `ResultsViewerSettingsPage`.

## Security

- Bind `127.0.0.1` only; never a routable interface.
- **Disabled by default**; explicit opt-in via a viewer setting.
- No token (localhost trust boundary is enough per decision).
- The endpoint can both read logs and act in the app, but every action mirrors a button the
  user already has — no new destructive capability beyond the GUI.

## Concurrency notes

- Reads of the live `SqliteStorage` are lock-safe even during viewer use.
- If a search is mid-flight or the FTS index is building, viewer-dependent tools should report
  a "loading/indexing" status rather than block.
- v1 is read-mostly for *internal* state: the agent drives via the same code paths the UI uses;
  it does not poke private fields.

## Out of scope for v1 (possible follow-ups)

- Standalone (non-GUI) host reusing the same tool layer.
- Driving multiple viewer windows (v1 targets the active/last-registered viewer).
- Persisting tags; filter-to-tagged.
- Time-window chunking for Kusto pulls too large even for `notruncation`.
