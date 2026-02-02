# UML Diagram Installation & PNG Generation Tests

## Overview

These integration tests validate that your Mermaid CLI and PlantUML installations work correctly and can actually generate PNG diagrams from source files.

## Test Files

- **`MermaidInstallationIntegrationTests.cs`** - Tests Mermaid CLI installation and PNG generation
- **`PlantUmlInstallationIntegrationTests.cs`** - Tests PlantUML installation and PNG generation

## Prerequisites

### For Mermaid Tests
1. **Node.js & npm** - [Download from nodejs.org](https://nodejs.org/)
2. **Mermaid CLI** - Install with:
   ```bash
   npm install -g @mermaid-js/mermaid-cli
   ```
3. **Verify installation**:
   ```bash
   mmdc --version
   ```

### For PlantUML Tests
1. **Java** - [Download from oracle.com](https://www.oracle.com/java/technologies/downloads/)
2. **PlantUML JAR** - [Download from plantuml.com](https://plantuml.com/download)
3. **Set up path**:
   - Either add to `PATH` environment variable
   - Or set `PLANTUML_JAR` environment variable

## Running the Tests

### All tests
```bash
dotnet test FindNeedleUmlDslTests --filter "IntegrationTests"
```

### Just Mermaid tests
```bash
dotnet test FindNeedleUmlDslTests --filter "MermaidInstallationIntegrationTests"
```

### Just PlantUML tests
```bash
dotnet test FindNeedleUmlDslTests --filter "PlantUmlInstallationIntegrationTests"
```

### Specific test
```bash
dotnet test FindNeedleUmlDslTests --filter "MermaidInstallationIntegrationTests.MermaidInstaller_CanGeneratePng_SimpleFlowchart"
```

### With verbose output
```bash
dotnet test FindNeedleUmlDslTests --filter "IntegrationTests" --verbosity detailed
```

## What Each Test Does

### Mermaid Tests

| Test | Purpose |
|------|---------|
| `ChecksStatus` | Verifies installer can read Mermaid status |
| `RequiresMermaidCliInstalled` | Skips if Mermaid not installed (diagnostic) |
| `CanFindMermaidCli` | Finds mmdc executable path |
| `CanGeneratePng_SimpleFlowchart` | Generates PNG from simple flow diagram |
| `CanGeneratePng_ComplexDiagram` | Generates PNG from sequence diagram |
| `CanGeneratePng_ClassDiagram` | Generates PNG from class diagram |
| `CanGenerateMultiplePngs` | Tests batch PNG generation |
| `InvalidDiagram_FailsGracefully` | Verifies error handling for invalid syntax |
| `GeneratedPngIsValidImage` | Validates PNG file format and content |
| `DiagnosticInfo` | Outputs installation info (no assertions) |

### PlantUML Tests

| Test | Purpose |
|------|---------|
| `ChecksStatus` | Verifies installer can read PlantUML status |
| `RequiresJavaAndPlantUml` | Skips if PlantUML not installed (diagnostic) |
| `CanFindPlantUmlJar` | Finds PlantUML JAR file path |
| `CanGeneratePng_SimpleFlowchart` | Generates PNG from activity diagram |
| `CanGeneratePng_SequenceDiagram` | Generates PNG from sequence diagram |
| `CanGeneratePng_ClassDiagram` | Generates PNG from class diagram |
| `CanGenerateMultiplePngs` | Tests batch PNG generation |
| `InvalidDiagram_FailsGracefully` | Verifies error handling for invalid syntax |
| `GeneratedPngIsValidImage` | Validates PNG file format and content |
| `DiagnosticInfo` | Outputs installation info (no assertions) |

## Output

Tests output diagnostic information to console:

### Example Mermaid Output
```
Mermaid Status: Mermaid CLI
Is Installed: True
Installed Version: 10.8.0
Installed Path: C:\Users\user\AppData\Roaming\npm\mmdc.cmd

? PNG generated successfully
   Size: 2543 bytes
   Location: C:\Temp\MermaidIntegrationTests_abc123\simple.png
```

### Example PlantUML Output
```
PlantUML Status: PlantUML
Is Installed: True
Installed Version: 1.2024.7
Installed Path: C:\plantuml\plantuml.jar

? PNG generated successfully
   Size: 3891 bytes
   Location: C:\Temp\PlantUmlIntegrationTests_def456\sequence.png
```

## Test Behavior

### If Tool Not Installed
Tests will skip with `AssertInconclusiveException` and show installation instructions:
```
Mermaid CLI is not installed. Install it from: https://github.com/mermaid-js/mermaid-cli
Or run: npm install -g @mermaid-js/mermaid-cli
```

### If Tool Installed
Tests will:
1. Create test diagrams in temp directory
2. Run the tool to generate PNGs
3. Validate PNG files were created
4. Verify PNG file format (PNG header signature)
5. Clean up temp files

### PNG Validation
Tests verify:
- ? PNG file exists
- ? PNG has content (> 0 bytes)
- ? PNG header is valid (starts with `89 50 4E 47`)
- ? File size is reasonable

## Troubleshooting

### "Could not find mmdc"
```bash
# Verify Mermaid CLI is in PATH
mmdc --version

# If not found, reinstall:
npm install -g @mermaid-js/mermaid-cli
```

### "Could not find java"
```bash
# Verify Java is in PATH
java -version

# If not found, install Java or add to PATH
```

### "PlantUML JAR not found"
```bash
# Set environment variable (Windows)
SET PLANTUML_JAR=C:\path\to\plantuml.jar

# Or add to PATH and set in PlantUmlInstaller
```

### "PNG generation failed"
1. Test if tool works manually:
   - **Mermaid**: `mmdc -i test.mmd -o test.png`
   - **PlantUML**: `java -jar plantuml.jar test.puml`
2. Check diagram syntax
3. Verify temp directory is writable

## CI/CD Integration

### Skip Integration Tests in CI
```bash
dotnet test --filter "Category!=Integration"
```

### Run Only Integration Tests
```bash
dotnet test --filter "MermaidInstallationIntegrationTests"
```

### Conditional Execution
Tests automatically skip with `AssertInconclusiveException` if tools not installed, so they're safe to run in CI pipelines that may not have these tools.

## Generated PNG Locations

Tests create temporary directories and clean up after themselves:
- **Location**: `%TEMP%\MermaidIntegrationTests_<GUID>` or `%TEMP%\PlantUmlIntegrationTests_<GUID>`
- **Auto-cleanup**: Yes, cleaned up after test completes
- **Retention**: If you need to inspect, check console output for path

## Example Test Output

```
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Running "MermaidInstaller_CanGeneratePng_SimpleFlowchart"
? PNG generated successfully
   Size: 2543 bytes
   Location: C:\Temp\MermaidIntegrationTests_a1b2c3d4\simple.png

Running "MermaidInstaller_DiagnosticInfo"
=== Mermaid Installation Diagnostic Info ===
Dependency Name: Mermaid CLI
Description: Node.js-based diagram generation
Is Installed: True
Installed Version: 10.8.0
Installed Path: C:\Users\user\AppData\Roaming\npm\mmdc.cmd

Test Run Successful.
Total tests: 10
Passed: 9
Skipped: 1
Failed: 0
```

## Notes

- Tests run independently - you can run individual tests
- Tests use real diagram syntax (valid Mermaid/PlantUML)
- Tests validate end-to-end: installation detection ? PNG generation ? PNG validation
- Temp files are automatically cleaned up
- Console output is helpful for debugging installation issues
