# UML DSL - Rule-Based Sequence Diagram Generation

The UML DSL (Domain Specific Language) allows you to define rules that automatically generate sequence diagrams from log messages. This document provides a comprehensive guide to creating rules and exercising all UML DSL features.

## Overview

The UML DSL uses a declarative JSON-based configuration to:
- Define participants (system components, users, services)
- Specify rules that match log messages
- Generate sequence diagrams in PlantUML or Mermaid syntax
- Create PNG images from diagrams (optional)

## Quick Start

### 1. Create a Rules File

Create a JSON file (e.g., `my-rules.rules.json`) with the following structure:

```json
{
  "title": "My System Flow",
  "participants": [
    {
      "id": "Client",
      "displayName": "Client Application",
      "type": "participant"
    },
    {
      "id": "Server",
      "displayName": "API Server",
      "type": "participant"
    },
    {
      "id": "DB",
      "displayName": "Database",
      "type": "participant"
    }
  ],
  "rules": [
    {
      "match": "Client request",
      "element": {
        "type": "message",
        "from": "Client",
        "to": "Server",
        "text": "Request"
      }
    },
    {
      "match": "Database query",
      "element": {
        "type": "message",
        "from": "Server",
        "to": "DB",
        "text": "SELECT * FROM users"
      }
    }
  ]
}
```

### 2. Use in RuleDSL Output Section

In your main RuleDSL configuration, add a UML output section:

```json
{
  "schemaVersion": "2.0",
  "title": "My Pipeline",
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
            "rulesFile": "my-rules.rules.json",
            "generateImage": true
          }
        }
      ]
    }
  ]
}
```

## Rule DSL Schema

### Root Level

| Property | Type | Required | Description |
|------|----|----|----|
| `title` | string | No | Diagram title |
| `participants` | array | No | List of participants in the diagram |
| `rules` | array | Yes | Rules that match log messages and generate diagram elements |

### Participants

Each participant defines a component in the diagram:

```json
{
  "id": "ComponentId",
  "displayName": "Component Display Name",
  "type": "participant"
}
```

**Properties:**
- `id` (required): Unique identifier used in rules
- `displayName` (optional): Human-readable name for the diagram
- `type` (optional): Either `"participant"` (default) or `"actor"` (for user roles)

### Rules

Rules match log messages and generate diagram elements:

```json
{
  "match": "error pattern",
  "element": {
    "type": "message",
    "from": "Participant1",
    "to": "Participant2",
    "text": "Message text"
  }
}
```

**Properties:**
- `match` (required): Regex pattern to match against log messages
- `element` (required): The diagram element to generate when the rule matches

### Elements

The `element` object defines what gets added to the diagram when a rule matches.

#### Message Element

Represents a communication between participants:

```json
{
  "type": "message",
  "from": "Participant1",
  "to": "Participant2",
  "text": "Request data",
  "arrowStyle": "solid"
}
```

**Properties:**
- `type`: `"message"` (required)
- `from` (required): Source participant ID
- `to` (required): Target participant ID
- `text` (optional): Message label
- `arrowStyle` (optional): `"solid"` (default), `"dashed"`, `"async"`

#### Activate Element

Activates a participant (shows them as active in the diagram):

```json
{
  "type": "activate",
  "from": "Participant1"
}
```

**Properties:**
- `type`: `"activate"` (required)
- `from` (required): Participant to activate

#### Deactivate Element

Deactivates a participant:

```json
{
  "type": "deactivate",
  "from": "Participant1"
}
```

**Properties:**
- `type`: `"deactivate"` (required)
- `from` (required): Participant to deactivate

#### Note Element

Adds a note to the diagram:

```json
{
  "type": "note",
  "position": "left",
  "text": "Important note"
}
```

**Properties:**
- `type`: `"note"` (required)
- `position` (optional): `"left"` or `"right"`
- `text` (required): Note content

#### Divider Element

Adds a visual divider/section:

```json
{
  "type": "divider",
  "text": "Phase 1"
}
```

**Properties:**
- `type`: `"divider"` (required)
- `text` (optional): Divider label

#### Group End Element

Ends a grouped block:

```json
{
  "type": "groupend"
}
```

**Properties:**
- `type`: `"groupend"` (required)

## Complete Example

Here's a complete example that demonstrates multiple features:

```json
{
  "title": "User Authentication Flow",
  "participants": [
    {
      "id": "User",
      "displayName": "End User",
      "type": "actor"
    },
    {
      "id": "Client",
      "displayName": "Web Client",
      "type": "participant"
    },
    {
      "id": "AuthServer",
      "displayName": "Authentication Server",
      "type": "participant"
    },
    {
      "id": "DB",
      "displayName": "User Database",
      "type": "participant"
    }
  ],
  "rules": [
    {
      "match": "User login",
      "element": {
        "type": "activate",
        "from": "Client"
      }
    },
    {
      "match": "User login",
      "element": {
        "type": "message",
        "from": "Client",
        "to": "AuthServer",
        "text": "POST /login",
        "arrowStyle": "solid"
      }
    },
    {
      "match": "Database query",
      "element": {
        "type": "message",
        "from": "AuthServer",
        "to": "DB",
        "text": "SELECT * FROM users WHERE email=?"
      }
    },
    {
      "match": "User found",
      "element": {
        "type": "message",
        "from": "DB",
        "to": "AuthServer",
        "text": "User data"
      }
    },
    {
      "match": "Authentication successful",
      "element": {
        "type": "message",
        "from": "AuthServer",
        "to": "Client",
        "text": "200 OK + token"
      }
    },
    {
      "match": "Login complete",
      "element": {
        "type": "deactivate",
        "from": "Client"
      }
    },
    {
      "match": "Error",
      "element": {
        "type": "note",
        "position": "left",
        "text": "Error occurred"
      }
    }
  ]
}
```

## Syntax Options

### PlantUML

PlantUML provides more advanced diagram features:

```json
{
  "action": {
    "type": "uml",
    "syntax": "plantuml",
    "path": "output.pu",
    "rulesFile": "my-rules.rules.json"
  }
}
```

**Features:**
- More complex participant types
- Advanced arrow styles
- Better support for nested groups

### Mermaid

Mermaid is simpler and renders directly in browsers:

```json
{
  "action": {
    "type": "uml",
    "syntax": "mermaid",
    "path": "output.mmd",
    "rulesFile": "my-rules.rules.json"
  }
}
```

**Features:**
- Simpler syntax
- Native browser rendering
- Lightweight

## Generating Images

To generate PNG images from your diagrams:

```json
{
  "action": {
    "type": "uml",
    "syntax": "mermaid",
    "path": "output.mmd",
    "rulesFile": "my-rules.rules.json",
    "generateImage": true
  }
}
```

**Requirements:**
- **Mermaid**: Install Mermaid CLI (`mmdc`) via the Diagram Tools page
- **PlantUML**: Install Java and PlantUML JAR via the Diagram Tools page

## Testing Your Rules

### 1. Test with Sample Data

Create a test file with sample log messages:

```
2024-01-01 10:00:00 User login attempt
2024-01-01 10:00:01 Database query executed
2024-01-01 10:00:02 Authentication successful
```

### 2. Run the Search

Execute your search with the RuleDSL configuration that includes the UML output section.

### 3. Verify Output

Check the generated files:
- `output.mmd` or `output.pu`: The diagram source file
- `output.png`: The generated image (if `generateImage` is true)

## Troubleshooting

### Empty Diagram

If your diagram is empty:
1. Check that your `match` patterns are correct (regex)
2. Verify log messages contain the expected text
3. Check the application logs for UML generation errors

### Installation Errors

If image generation fails:
1. Install Mermaid CLI or PlantUML via the Diagram Tools page
2. Verify the installation completed successfully
3. Check that the tools are in your PATH

### Rule Not Matching

If rules aren't matching:
1. Test your regex pattern with a regex tester
2. Check that the log message text is what you expect
3. Use case-insensitive matching: `"match": "(?i)error"`

## Advanced Features

### Multiple Rules for Same Message

You can have multiple rules that match the same log message:

```json
{
  "rules": [
    {
      "match": "Request received",
      "element": { "type": "activate", "from": "Server" }
    },
    {
      "match": "Request received",
      "element": { "type": "message", "from": "Client", "to": "Server" }
    }
  ]
}
```

### Placeholder Substitution

Use placeholders in your element text:

```json
{
  "match": "User (.+) logged in",
  "element": {
    "type": "message",
    "from": "Client",
    "to": "AuthServer",
    "text": "Login for {0}"
  }
}
```

The `{0}` will be replaced with the first capture group from the regex.

### Conditional Rules

Use regex alternation to match multiple patterns:

```json
{
  "match": "error|failed|exception",
  "element": {
    "type": "note",
    "position": "left",
    "text": "Error detected"
  }
}
```

## Files and Locations

### Rules File

The rules file can be:
- A full path: `C:\path\to\my-rules.rules.json`
- Relative to app directory: `rules/my-rules.rules.json`
- Relative to working directory: `./my-rules.rules.json`

### Output Files

- **Diagram source**: `.mmd` (Mermaid) or `.pu` (PlantUML)
- **Generated image**: `.png` (same base name as source)

## Summary

The UML DSL provides a powerful way to automatically generate sequence diagrams from log data. By defining rules that match log messages, you can create visual representations of system flows, making it easier to understand complex interactions.

For more examples, see the `FindNeedleUmlDsl/Examples/` directory.
