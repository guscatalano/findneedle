# UML DSL Quick Start Guide

## 1. Create a Rules File

Create `my-rules.rules.json`:

```json
{
  "title": "My Flow",
  "participants": [
    { "id": "A", "displayName": "Component A" },
    { "id": "B", "displayName": "Component B" }
  ],
  "rules": [
    {
      "match": "start",
      "element": { "type": "message", "from": "A", "to": "B", "text": "Start" }
    }
  ]
}
```

## 2. Add to RuleDSL Configuration

```json
{
  "schemaVersion": "2.0",
  "sections": [
    {
      "name": "UML Output",
      "purpose": "uml",
      "rules": [
        {
          "action": {
            "type": "uml",
            "syntax": "mermaid",
            "path": "output.mmd",
            "rulesFile": "my-rules.rules.json"
          }
        }
      ]
    }
  ]
}
```

## 3. Run Your Search

Execute your search with the RuleDSL configuration.

## 4. View Results

- `output.mmd`: Diagram source (Mermaid syntax)
- `output.png`: Generated image (if `generateImage: true`)

## Element Types

| Type | Description | Required Properties |
|----|----|----|
| `message` | Communication between participants | `from`, `to` |
| `activate` | Activate a participant | `from` |
| `deactivate` | Deactivate a participant | `from` |
| `note` | Add a note | `text` |
| `divider` | Add a section divider | (none) |
| `groupend` | End a grouped block | (none) |

## Quick Examples

### Simple Flow
```json
{
  "rules": [
    {
      "match": "request",
      "element": { "type": "message", "from": "Client", "to": "Server", "text": "GET /api" }
    }
  ]
}
```

### With Activation
```json
{
  "rules": [
    { "match": "start", "element": { "type": "activate", "from": "Server" } },
    { "match": "request", "element": { "type": "message", "from": "Client", "to": "Server" } },
    { "match": "done", "element": { "type": "deactivate", "from": "Server" } }
  ]
}
```

### With Notes
```json
{
  "rules": [
    { "match": "error", "element": { "type": "note", "position": "left", "text": "Error!" } }
  ]
}
```

## Syntax Options

- **Mermaid**: `syntax: "mermaid"` - Simpler, browser-native
- **PlantUML**: `syntax: "plantuml"` - More advanced features

## Generate Images

Add `"generateImage": true` to your action:

```json
{
  "action": {
    "type": "uml",
    "syntax": "mermaid",
    "path": "output.mmd",
    "generateImage": true
  }
}
```

## Need More Help?

See `README.md` for complete documentation.
