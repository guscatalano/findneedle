# findneedle Command-Line Tool

A fast log search utility for Windows with RuleDSL-based configuration.

## Synopsis

```
findneedle <folder-or-file> [options]
```

- **`<folder-or-file>`** (positional, required unless a `--rules` file supplies inputs) — the log source
  to search: a folder (scanned for supported files) or a single file. Supported inputs include `.etl`
  (ETW/WPP), `.evtx`/Event Log, `.log`/`.txt`, `.csv`, `.json`, `.zip`, `.cab`, `.pcap`/`.pcapng`.
- Run from source with `dotnet run --project findneedle.csproj -- <args>`, or invoke the built
  `findneedle.exe` directly. **When installed from the Microsoft Store, `findneedle` is registered as an
  app-execution alias**, so it's callable from any terminal.

## Quick Start

### Basic Usage

```bash
# Search a folder
dotnet run --project findneedle.csproj C:\Logs

# Search a specific file
dotnet run --project findneedle.csproj C:\Logs\app.log

# Use a RuleDSL configuration file
dotnet run --project findneedle.csproj -- --rules my-rules.rules.json C:\Logs
```

### Command-Line Options

| Option | Description | Example |
|------|----|----|
| `--rules=<file>` | Path to RuleDSL rules file | `--rules=my-rules.rules.json` |
| `--out=<fmt>` | Write decoded records to a file: `csv`, `json`, `txt`, or `html` | `--out=json` |
| `--out-file=<path>` | Override the output path (default `output/findneedle_decoded.<ext>`) | `--out-file=rows.csv` |
| `--symbols=<path>` | PDB folders / symbol servers for WPP symbol provisioning | `--symbols=C:\syms` |
| `--symbol-source=<dirs>` | Extra folders to sweep for binaries + PDBs | `--symbol-source=C:\bins` |
| `--wpp-decoder=<mode>` | WPP decoder: `tracefmt` (WDK reference), `managed`, `auto` (default), `compare` | `--wpp-decoder=tracefmt` |
| `--verbose`, `-v` | Show detailed output | `--verbose` |
| `--force`, `-f`, `--yes`, `-y` | Skip the "press enter to search" confirmation (for scripting) | `--force` |
| `--clear-output`, `-c` | Clear the `output/` folder before running | `--clear-output` |

**Where output goes:** RuleDSL/UML outputs and `--out` files are written under an **`output/` folder in
your current working directory** (not next to the exe — the exe's folder is read-only when installed from
the Store). Use `--out-file=<path>` to write a decoded dump somewhere specific.

**Exit codes** (set from the WPP decode-proof; see below): `0` fully decoded · `1` symbols still missing ·
`2` nothing decoded. Non-interactive/scripted runs should pass `--force`.

### Decoding a WPP `.etl` and proving it decoded

```bash
# Decode a WPP trace, resolving symbols from a store, and dump the rows as JSON
findneedle C:\Traces --symbols=C:\symbols --out=json --force
```

The tool prints a **decode summary** and sets an **exit code** you can assert on — useful when
authoring a custom `ISymbolResolver` and testing it end-to-end:

- `0` — fully decoded (no unresolved WPP symbols)
- `1` — decoded, but symbols are still missing (provisioning ran but couldn't resolve everything)
- `2` — no rows decoded

Add `--out=<fmt>` to also write the decoded rows so you can *see* what came out, not just the exit code.

By default (`--wpp-decoder=auto`) the tool uses the WDK's **tracefmt** — the reference WPP decoder — when
it's installed, falling back to the built-in managed decoder when it isn't. Force one with
`--wpp-decoder=tracefmt` (validate against the reference) or `--wpp-decoder=managed` (no WDK needed).

### RuleDSL Rules File

Create a `rules.json` file:

```json
{
  "schemaVersion": "2.0",
  "title": "My Pipeline",
  "inputs": [
    {
      "type": "folder",
      "path": "C:\\Logs",
      "depth": "Intermediate"
    }
  ],
  "sections": [
    {
      "name": "ErrorFilter",
      "purpose": "filter",
      "rules": [
        {
          "field": "level",
          "match": "ERROR|CRITICAL",
          "actions": [{ "type": "include" }]
        }
      ]
    },
    {
      "name": "UML Output",
      "purpose": "uml",
      "rules": [
        {
          "action": {
            "type": "uml",
            "syntax": "mermaid",
            "path": "output.mmd",
            "rulesFile": "my-uml-rules.rules.json",
            "generateImage": true
          }
        }
      ]
    }
  ]
}
```

### UML DSL Rules File

Create a UML rules file (`my-uml-rules.rules.json`):

```json
{
  "title": "System Flow",
  "participants": [
    { "id": "Client", "displayName": "Client" },
    { "id": "Server", "displayName": "Server" }
  ],
  "rules": [
    {
      "match": "request",
      "element": {
        "type": "message",
        "from": "Client",
        "to": "Server",
        "text": "GET /api"
      }
    }
  ]
}
```

## Complete Example

### 1. Create RuleDSL Config (`pipeline.rules.json`)

```json
{
  "schemaVersion": "2.0",
  "title": "Log Analysis Pipeline",
  "inputs": [
    {
      "type": "folder",
      "path": "C:\\MyLogs",
      "depth": "Intermediate"
    }
  ],
  "sections": [
    {
      "name": "FilterErrors",
      "purpose": "filter",
      "rules": [
        {
          "field": "level",
          "match": "ERROR|CRITICAL",
          "actions": [{ "type": "include" }]
        }
      ]
    },
    {
      "name": "GenerateUML",
      "purpose": "uml",
      "rules": [
        {
          "action": {
            "type": "uml",
            "syntax": "mermaid",
            "path": "output\\flow.mmd",
            "rulesFile": "uml-rules.rules.json",
            "generateImage": true
          }
        }
      ]
    }
  ]
}
```

### 2. Create UML Rules (`uml-rules.rules.json`)

```json
{
  "title": "Error Flow",
  "participants": [
    { "id": "App", "displayName": "Application" },
    { "id": "DB", "displayName": "Database" }
  ],
  "rules": [
    {
      "match": "database error",
      "element": {
        "type": "message",
        "from": "App",
        "to": "DB",
        "text": "Query failed"
      }
    }
  ]
}
```

### 3. Run the Search

```bash
dotnet run --project findneedle.csproj -- --rules pipeline.rules.json --force
```

### 4. View Results

- **Search results**: CSV/JSON files in `output/` folder
- **UML diagram**: `output/flow.mmd` (Mermaid source)
- **UML image**: `output/flow.png` (if `generateImage: true`)

## Advanced Options

### Multiple Rule Files

You can specify multiple rule files:

```bash
dotnet run --project findneedle.csproj -- --rules pipeline1.rules.json --rules pipeline2.rules.json
```

### Verbose Output

For detailed logging:

```bash
dotnet run --project findneedle.csproj -- --verbose --rules my-rules.json
```

### Force Mode (Scripting)

For non-interactive use:

```bash
dotnet run --project findneedle.csproj -- --force --rules my-rules.json
```

### Clear Output

Clear existing output before running:

```bash
dotnet run --project findneedle.csproj -- --clear-output --rules my-rules.json
```

## Output Files

The tool generates output files in the `output/` folder:

| File | Description |
|----|----|
| `*.csv` | Search results in CSV format |
| `*.json` | Search results in JSON format |
| `*.mmd` | Mermaid UML diagram source |
| `*.pu` | PlantUML diagram source |
| `*.png` | Generated UML images |

## Troubleshooting

### Rules Not Applied

- Verify the rules file path is correct
- Check that the rules file is valid JSON
- Use `--verbose` to see detailed logs

### UML Generation Fails

- Install Mermaid CLI or PlantUML via the Diagram Tools page
- Or use the bundled installers when prompted

### No Results

- Check your filter rules match the log content
- Verify the input locations contain log files
- Use `--verbose` to see what files are being searched

## See Also

- `../FindNeedleRuleDSL/README.md` - Complete RuleDSL documentation
- `../FindNeedleUmlDsl/README.md` - UML DSL documentation
- `../FindNeedleUmlDsl/QUICK_START.md` - UML DSL quick start
