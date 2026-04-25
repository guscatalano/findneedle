# findneedle Command-Line Tool

A fast log search utility for Windows with RuleDSL-based configuration.

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
| `--verbose`, `-v` | Show detailed output | `--verbose` |
| `--force`, `-f`, `--yes`, `-y` | Skip confirmation prompts | `--force` |
| `--clear-output`, `-c` | Clear existing output before running | `--clear-output` |

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
