# findneedle

[![Code Coverage](https://github.com/guscatalano/findneedle/raw/master/.github/badges/coverage.svg)](https://github.com/guscatalano/findneedle/actions/workflows/dotnet-desktop.yml)
![Test Status](https://github.com/guscatalano/findneedle/raw/master/.github/badges/test-status.svg)
![Test Duration](https://github.com/guscatalano/findneedle/raw/master/.github/badges/test-duration.svg)

**A tool to quickly search through logs in Windows.**

---

## Overview

**findneedle** is a lightweight utility designed to help you efficiently search through log files on Windows systems. With a focus on speed and simplicity, it offers a clean user interface and powerful search capabilities.

## Features

- 🔍 Fast text search across large log files
- 🖥️ Simple, user-friendly interface
- 📂 Support for multiple log file formats
- ⏩ Real-time filtering and highlighting
- ⚡ Built with JavaScript, C#, HTML, and CSS

## Getting Started

### Prerequisites

- Windows OS
- [.NET Core SDK](https://dotnet.microsoft.com/download) (for C# components)

### Installation

1. **Clone the repository:**  
   `git clone https://github.com/guscatalano/findneedle.git`
2. **Navigate into the directory:**  
   `cd findneedle`
3. **Build or open the project:**
   - Open in Visual Studio or Visual Studio Code.
   - Build the solution using the .NET Core SDK.
4. **Run the application:**  
   - Launch the executable, or run via `dotnet run` as appropriate for your project.

#### Or, get it directly from the Microsoft Store:

[![Download from the Microsoft Store](https://img.shields.io/badge/Microsoft%20Store-Findneedle-blue?logo=microsoft)](https://apps.microsoft.com/detail/9NWLTBV4NRDL?hl=en-us&gl=US&ocid=pdpshare)

## Usage

1. Launch the application.
2. Select or drag-and-drop log files.
3. Enter your search keyword(s).
4. View and filter results instantly.

## Architecture

### RuleDSL Configuration System (Primary)

findneedle uses a **RuleDSL** (Rule Domain Specific Language) for configuration, replacing the deprecated plugin-based system. RuleDSL is a JSON-based configuration system that allows you to define filters, enrichments, and outputs declaratively.

**Key Benefits:**
- 📝 Declarative configuration in JSON format
- 🔧 No code changes or rebuilds required
- 🔄 Easy to share and version-control rules
- 🎯 Support for filter, enrichment, and output rules

**Quick Start:**
1. Create a rules file (e.g., `my-rules.rules.json`)
2. Define your rules in the `sections` array
3. Run your search with the rules file

**Example:**
```json
{
  "schemaVersion": "2.0",
  "title": "Error Detection",
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
    }
  ]
}
```

See `FindNeedleRuleDSL/README.md` for complete documentation.

### UML DSL - Sequence Diagram Generation

The UML DSL also includes a powerful sequence diagram generation system that creates visual representations of log flows.

**Key Features:**
- 📊 Generate sequence diagrams from log messages
- 🎨 Support for PlantUML and Mermaid syntax
- 🖼️ Optional PNG image generation
- 📝 Declarative rule-based configuration

**Quick Start:**
1. Create a rules file (e.g., `my-rules.rules.json`) defining participants and rules
2. Add a UML output section to your RuleDSL configuration
3. Run your search to generate diagrams

**Examples:**
- `FindNeedleUmlDsl/Examples/sample-uml.rules.json` - Web login flow with activations and notes
- `FindNeedleUmlDsl/Examples/session-management.rules.json` - Session management flow

See `FindNeedleUmlDsl/README.md` for complete documentation.

### Plugin System (Deprecated)

findneedle features a flexible plugin system that allows users to extend and customize how logs are processed and searched. Plugins can be developed independently and integrated with the application to add new functionality, such as custom log parsers, advanced filtering, or integration with external tools.

- **How it works:**  
  Plugins are loaded at runtime and can interact with the application via well-defined interfaces. This enables developers to add or modify features without changing the core codebase.

- **Creating a Plugin:**  
  To create a plugin, implement the required interface (see the `/plugins` directory for examples) and register your plugin with the application. The system will automatically detect and initialize available plugins on startup.

**Note:** The plugin system is deprecated in favor of RuleDSL. See `DEPRECATED_PLUGINS_MIGRATION.md` for migration guidance.

### Input, Output, and Processor System

findneedle is architected around three main extensible component types: Inputs, Outputs, and Processors.

#### Inputs

Inputs are responsible for acquiring data (such as log files) and feeding them into the processing pipeline. Examples include reading from local files, directories, or even remote sources.

#### Processors

Processors take the raw input data and apply transformations or analyses. This can include parsing, filtering, or enriching log entries. Multiple processors can be chained to build complex processing pipelines.

#### Outputs

Outputs handle the final presentation or export of processed data.

