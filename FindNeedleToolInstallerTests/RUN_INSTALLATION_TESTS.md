# Run UML Installation Tests

Quick commands to validate your UML diagram generation setup.

## Quick Start

### Test Everything
```bash
dotnet test FindNeedleUmlDslTests --filter "IntegrationTests" --verbosity normal
```

### Test Mermaid Only
```bash
dotnet test FindNeedleUmlDslTests --filter "MermaidInstallationIntegrationTests" --verbosity detailed
```

### Test PlantUML Only
```bash
dotnet test FindNeedleUmlDslTests --filter "PlantUmlInstallationIntegrationTests" --verbosity detailed
```

---

## Diagnostic Tests (Check Installation)

These tests don't require the tools to be fully installed - they just check status:

```bash
# Check Mermaid installation status
dotnet test FindNeedleUmlDslTests --filter "MermaidInstallationIntegrationTests.MermaidInstaller_DiagnosticInfo"

# Check PlantUML installation status
dotnet test FindNeedleUmlDslTests --filter "PlantUmlInstallationIntegrationTests.PlantUmlInstaller_DiagnosticInfo"
```

---

## PNG Generation Tests

These require the tools to be installed:

```bash
# Test Mermaid PNG generation (simple diagram)
dotnet test FindNeedleUmlDslTests --filter "MermaidInstallationIntegrationTests.MermaidInstaller_CanGeneratePng_SimpleFlowchart"

# Test Mermaid PNG generation (complex diagram)
dotnet test FindNeedleUmlDslTests --filter "MermaidInstallationIntegrationTests.MermaidInstaller_CanGeneratePng_ComplexDiagram"

# Test PlantUML PNG generation (simple diagram)
dotnet test FindNeedleUmlDslTests --filter "PlantUmlInstallationIntegrationTests.PlantUmlInstaller_CanGeneratePng_SimpleFlowchart"

# Test PlantUML PNG generation (sequence diagram)
dotnet test FindNeedleUmlDslTests --filter "PlantUmlInstallationIntegrationTests.PlantUmlInstaller_CanGeneratePng_SequenceDiagram"
```

---

## Batch Processing Tests

```bash
# Test multiple PNG generation
dotnet test FindNeedleUmlDslTests --filter "MermaidInstallationIntegrationTests.MermaidInstaller_CanGenerateMultiplePngs"

dotnet test FindNeedleUmlDslTests --filter "PlantUmlInstallationIntegrationTests.PlantUmlInstaller_CanGenerateMultiplePngs"
```

---

## Validation Tests

These verify the generated files are valid PNGs:

```bash
# Mermaid PNG validation
dotnet test FindNeedleUmlDslTests --filter "MermaidInstallationIntegrationTests.MermaidInstaller_GeneratedPngIsValidImage"

# PlantUML PNG validation
dotnet test FindNeedleUmlDslTests --filter "PlantUmlInstallationIntegrationTests.PlantUmlInstaller_GeneratedPngIsValidImage"
```

---

## Error Handling Tests

These verify the tools handle invalid input gracefully:

```bash
# Mermaid error handling
dotnet test FindNeedleUmlDslTests --filter "MermaidInstallationIntegrationTests.MermaidInstaller_InvalidDiagram_FailsGracefully"

# PlantUML error handling
dotnet test FindNeedleUmlDslTests --filter "PlantUmlInstallationIntegrationTests.PlantUmlInstaller_InvalidDiagram_FailsGracefully"
```

---

## Installation Guides

If tests fail with "not installed", follow these steps:

### Install Mermaid CLI
```bash
# 1. Ensure Node.js is installed
node --version

# 2. Install Mermaid CLI globally
npm install -g @mermaid-js/mermaid-cli

# 3. Verify
mmdc --version
```

### Install PlantUML
```bash
# 1. Ensure Java is installed
java -version

# 2. Download PlantUML
# Visit: https://plantuml.com/download
# Download plantuml.jar and save to a convenient location

# 3. Add to environment:
# Set PLANTUML_JAR=C:\path\to\plantuml.jar

# 4. Verify
java -jar C:\path\to\plantuml.jar -version
```

---

## Understanding Test Output

### ? Test Passed
```
? PNG generated successfully
   Size: 2543 bytes
   Location: C:\Temp\...\simple.png
```
This means:
- Tool is installed and working
- PNG was created
- File has valid content
- Everything OK!

### ?? Test Skipped
```
AssertInconclusiveException: Mermaid CLI is not installed.
Install it from: npm install -g @mermaid-js/mermaid-cli
```
This means:
- Tool is not installed
- Test was skipped (not failed)
- Installation instructions are shown

### ? Test Failed
```
AssertionError: PNG file should be created at ...
```
This means:
- Tool is installed but something went wrong
- Check tool installation
- Check temp directory permissions
- Verify diagram syntax

---

## Quick Health Check

Run this to get a complete overview:

```bash
dotnet test FindNeedleUmlDslTests --filter "DiagnosticInfo" --verbosity detailed
```

This will:
- Show Mermaid installation status
- Show PlantUML installation status
- Display file paths
- Show versions (if installed)
- Suggest next steps (if not installed)

---

## Notes

- Tests automatically skip if tools not installed
- PNG files are created in temp directory
- Temp files are automatically cleaned up
- Each test run creates a fresh temp directory
- Tests are safe to run multiple times
- All tests are independent

---

## Visual Diagram Examples Tested

### Mermaid
- **Flowchart** - Simple flow with start/process/end
- **Sequence** - Multi-actor communication flow
- **Class** - Object-oriented class relationships
- **Pie Chart** - Pie chart data visualization
- **Gantt** - Project timeline chart

### PlantUML
- **Activity** - Flowchart with action states
- **Sequence** - Interaction between actors
- **Class** - UML class diagrams with inheritance
- **Use Case** - Use case diagrams
- **State** - State machine diagrams

---

## Troubleshooting

### "Command not found: mmdc"
```bash
npm install -g @mermaid-js/mermaid-cli
```

### "Command not found: java"
Install Java from https://www.oracle.com/java/

### "No permission to write temp files"
Check that `%TEMP%` directory is writable

### "PNG looks corrupted"
- Reinstall the tool
- Check system resources
- Try a simpler diagram

---

For more details, see: `UML_INSTALLATION_TESTS.md`
