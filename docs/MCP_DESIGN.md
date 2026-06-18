# MCP Server Design — Live Viewer Control

Status: **implemented (v1).** See "Implementation" at the bottom for what shipped and how to use it.

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
- `tag_row(id, tag, text?)` / `clear_tag(id)` — tag = category (Important/Question/Resolved/Note)
  plus an optional free-text note the agent can set (shown in the row tooltip + details; note-only
  defaults the category to "Note")
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

## Implementation (what shipped)

Code lives in `FindNeedleUX/Services/Mcp/` plus a few touch points:

- **`McpServer`** — minimal MCP JSON-RPC over the BCL `HttpListener`, bound to `http://127.0.0.1:{port}/`
  (loopback-guarded; rejects non-loopback remotes). Implements `initialize`, `notifications/initialized`,
  `ping`, `tools/list`, `tools/call`. This is the request/response path of MCP "Streamable HTTP"; it
  does **not** push server-initiated messages, so a `GET` (SSE) is answered `405`. No ASP.NET Core /
  MCP-SDK dependency (the app is self-contained MSIX + ILRepack, so a BCL-only server is the safe fit).
- **`McpTools`** — the tool catalog (name + description + JSON schema + handler), each calling
  `McpViewerBridge`. Workspace: `list_locations`, `list_rules`, `add_folder`, `add_kusto`,
  `remove_location`, `run_search`, `cancel_search`. Viewer: `get_view`, `get_page`, `get_record`,
  `summary`, `histogram`, `search`, `set_filter`, `clear_filters`, `set_sort`, `goto_page`,
  `set_page_size`, `select_row`, `tag_row`, `clear_tag`, `set_details_mode`, `export`.
- **`McpViewerBridge`** — single live workspace (no session); workspace ops via `MiddleLayerService`
  marshaled to the UI thread, viewer ops delegated to the registered `IMcpViewerController`.
- **`IMcpViewerController`** — implemented by `NativeResultsPage`; registers on load / unregisters on
  unload; every method hops to the UI thread; returns plain DTOs.
- **`McpServerHost`** — starts/stops the server from settings, reacts to `Changed`, surfaces status.
- **Stable id** — `ISearchResult.GetRowId()` / `SqliteStorage.Id` / `LogLine.RowId`; row tags are now
  keyed by `RowId`.
- **Export** — `ResultExporter` (headless csv/json/xml) shared by the viewer's picker export and the
  MCP `export` tool (writes to a path / temp file, returns `{path, rowCount}`).

### Enabling / using it
- Settings → Result viewer settings → **MCP server (experimental)**: tick "Enable", optionally change
  the port (default **8765**). Off by default. App restart not required — the host starts/stops live.
- Endpoint: `http://127.0.0.1:8765/` (POST JSON-RPC). Point an MCP client that supports Streamable
  HTTP at it. Viewer tools need a result viewer open (run a search first), else they return a clear
  "no active view" error.

### Known limitations / follow-ups for v1
- **Packaging:** works when the app runs **unpackaged** (running the exe directly). An MSIX /
  AppContainer install blocks loopback by default and would need a loopback exemption.
- No SSE / server push, no session-id enforcement, no auth token (localhost-only by design).
- `select_row` only selects rows on the current page; `tag_row` re-publishes the page to show the glyph.

## Out of scope for v1 (possible follow-ups)

- Standalone (non-GUI) host reusing the same tool layer.
- Driving multiple viewer windows (v1 targets the active/last-registered viewer).
- Persisting tags; filter-to-tagged.
- Time-window chunking for Kusto pulls too large even for `notruncation`.
