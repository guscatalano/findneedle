# WPP cat sample — real WPP `.etl` fixture generator

Generates a **genuine WPP** (Windows software trace preprocessor) ETL fixture so the `tracefmt`
decode path in `ETWPlugin` can be tested against real WPP data.

## Why this exists

The other ETL fixtures (`cats-5M.etl`, `large-5M.etl`) are produced by a .NET `EventSource`, which
emits **self-describing** (manifest / TraceLogging) events — `TraceEvent` decodes them with no help.
**WPP is different**: it's a *compile-time* mechanism. `tracewpp.exe` rewrites the `DoTraceMessage(...)`
calls in `wppcat.c` into per-call message GUIDs, and decoding requires the **TMF** (Trace Message
Format) extracted from the binary's PDB and handed to `tracefmt.exe`. Managed `EventSource` cannot
produce WPP records, so this needs a real native WPP producer.

## What it produces

Into `<repo>/LargeSamples/` (gitignored — reproduce by re-running):
- `cats-wpp.etl` — a real WPP capture (`cat #N breed=… color=…`, with a `NAME=Mittens` needle).
- `wpp-tmf/*.tmf` — the TMF needed to decode it.

## Requirements

- Windows SDK/WDK tools: `tracewpp`, `tracefmt`, `tracelog`, `tracepdb` (auto-located).
- MSVC C++ toolset (auto-located via `vswhere`).
- **Admin** (ETW session). Run elevated.

## Run

```powershell
# quick validate (default 5,000 messages)
pwsh -File tools\WppCatSample\build-wpp-cat.ps1

# the big one (matches cats-5M.etl scale)
pwsh -File tools\WppCatSample\build-wpp-cat.ps1 -Count 5000000
```

Pipeline: `tracewpp` (→ `wppcat.tmh`) → `cl` (→ `wppcat.exe` + PDB) → `tracepdb` (→ `.tmf`) →
`tracelog` capture while `wppcat.exe` runs → `tracefmt` verify.

## Using it to test FindNeedle's tracefmt path

`TraceFmt.ParseSimpleETL` runs `tracefmt.exe <etl>` and relies on `tracefmt` *finding* the TMF.
`tracefmt` searches `%TRACE_FORMAT_SEARCH_PATH%` (and the working dir). So before pointing FindNeedle
at `cats-wpp.etl`, make the TMF discoverable:

```powershell
$env:TRACE_FORMAT_SEARCH_PATH = "$PWD\LargeSamples\wpp-tmf"
```

Without a discoverable TMF, `tracefmt` still processes the ETL but every event formats as `Unknown`
(useful for testing that failure mode too).
