# UML Installation & PNG Generation Integration Tests - Summary

## ? What's Been Created

Two comprehensive integration test suites for validating UML diagram generation:

### **1. MermaidInstallationIntegrationTests.cs** (10 tests)
Tests for Mermaid CLI installation and PNG diagram generation.

**Key Tests:**
- ? Status detection and diagnostics
- ? Mermaid CLI availability check
- ? Simple flowchart PNG generation
- ? Complex sequence diagram PNG generation
- ? Class diagram PNG generation
- ? Multiple PNG batch generation
- ? Invalid diagram error handling
- ? PNG file format validation
- ? Complete diagnostic output

### **2. PlantUmlInstallationIntegrationTests.cs** (10 tests)
Tests for PlantUML installation and PNG diagram generation.

**Key Tests:**
- ? Status detection and diagnostics
- ? PlantUML JAR availability check
- ? Activity diagram PNG generation
- ? Sequence diagram PNG generation
- ? Class diagram PNG generation
- ? Multiple PNG batch generation
- ? Invalid diagram error handling
- ? PNG file format validation
- ? Complete diagnostic output

### **3. Documentation**
- ? `UML_INSTALLATION_TESTS.md` - Comprehensive test guide
- ? `RUN_INSTALLATION_TESTS.md` - Quick command reference

---

## ?? What Each Test Validates

### Installation Detection
```csharp
var status = installer.GetStatus();
// Returns: name, version, path, isInstalled, instructions
Assert.IsTrue(status.IsInstalled);
Assert.IsNotNull(status.InstalledPath);
```

### PNG Generation (Mermaid)
```
Test Diagram
    ?
mmd file created in temp
    ?
mmdc command executed
    ?
PNG file generated
    ?
PNG validated (header, size, content)
```

### PNG Generation (PlantUML)
```
Test Diagram
    ?
puml file created in temp
    ?
java -jar plantuml.jar executed
    ?
PNG file generated
    ?
PNG validated (header, size, content)
```

### Error Handling
```csharp
// Invalid diagram
File.WriteAllText("invalid.mmd", "invalid @#$ syntax");
int exitCode = RunMermaidCommand(...);
Assert.AreNotEqual(0, exitCode); // Should fail
```

### PNG Validation
```csharp
// Verify PNG file format
byte[] fileBytes = File.ReadAllBytes(pngFile);
Assert.AreEqual(0x89, fileBytes[0]); // PNG signature
Assert.AreEqual(0x50, fileBytes[1]);
Assert.AreEqual(0x4E, fileBytes[2]);
Assert.AreEqual(0x47, fileBytes[3]);
```

---

## ?? Running the Tests

### Quick Start
```bash
# Run all UML installation tests
dotnet test FindNeedleUmlDslTests --filter "IntegrationTests"
```

### Check Installation Status (No Requirements)
```bash
dotnet test FindNeedleUmlDslTests --filter "DiagnosticInfo"
```

### Test PNG Generation (Requires Tools)
```bash
# Mermaid only
dotnet test FindNeedleUmlDslTests --filter "MermaidInstallationIntegrationTests"

# PlantUML only
dotnet test FindNeedleUmlDslTests --filter "PlantUmlInstallationIntegrationTests"
```

### Run Specific Test
```bash
dotnet test FindNeedleUmlDslTests --filter "MermaidInstaller_CanGeneratePng_SimpleFlowchart"
```

---

## ?? Test Behavior

### If Tool NOT Installed
```
Test: MermaidInstaller_CanGeneratePng_SimpleFlowchart
Result: ?? SKIPPED (AssertInconclusiveException)
Message: Mermaid CLI is not installed
          Install it with: npm install -g @mermaid-js/mermaid-cli
```

### If Tool IS Installed
```
Test: MermaidInstaller_CanGeneratePng_SimpleFlowchart
Result: ? PASSED

Console Output:
  ? PNG generated successfully
     Size: 2543 bytes
     Location: C:\Temp\MermaidIntegrationTests_abc123\simple.png
```

### Error Cases
```
Test: MermaidInstaller_InvalidDiagram_FailsGracefully
Result: ? PASSED (Expected exit code: non-zero)

Console Output:
  ? Invalid diagram properly rejected (exit code: 1)
```

---

## ??? Installation Requirements

### For Mermaid Tests
```bash
# 1. Install Node.js
# Download from: https://nodejs.org/

# 2. Install Mermaid CLI
npm install -g @mermaid-js/mermaid-cli

# 3. Verify
mmdc --version
# Output: mermaid version 10.8.0
```

### For PlantUML Tests
```bash
# 1. Install Java (if not present)
java -version

# 2. Download PlantUML
# https://plantuml.com/download
# Save as: C:\plantuml\plantuml.jar

# 3. Test manually
java -jar C:\plantuml\plantuml.jar -version
```

---

## ?? Diagram Types Tested

### Mermaid Diagrams
| Type | Tested | Purpose |
|------|--------|---------|
| Flowchart | ? | Process flows |
| Sequence | ? | Actor interactions |
| Class | ? | OOP class hierarchies |
| Pie Chart | ? | Data visualization |
| Gantt | ? | Project timelines |

### PlantUML Diagrams
| Type | Tested | Purpose |
|------|--------|---------|
| Activity | ? | Workflow diagrams |
| Sequence | ? | Interaction diagrams |
| Class | ? | UML class diagrams |
| Use Case | ? | System use cases |
| State | ? | State machines |

---

## ?? Generated Test Artifacts

### Temporary Directories
```
C:\Users\[user]\AppData\Local\Temp\
??? MermaidIntegrationTests_[GUID]/
?   ??? simple.mmd
?   ??? simple.png ? Generated
?   ??? complex.mmd
?   ??? complex.png ? Generated
?   ??? ...
??? PlantUmlIntegrationTests_[GUID]/
    ??? sequence.puml
    ??? sequence.png ? Generated
    ??? ...
```

**Auto-cleanup:** Yes - deleted after test completes

---

## ? Key Features

### 1. **Smart Test Skipping**
- Detects if tools are installed
- Skips gracefully with helpful instructions
- Doesn't fail CI/CD pipelines

### 2. **Comprehensive Validation**
- Installation detection
- PNG file creation
- PNG file format validation (PNG header)
- File size checks
- Batch processing

### 3. **Realistic Diagrams**
- Uses actual diagram syntax
- Tests various diagram types
- Validates both simple and complex diagrams

### 4. **Error Handling**
- Tests invalid diagram detection
- Verifies exit codes
- Graceful failure reporting

### 5. **Diagnostic Output**
- Installation status
- Version information
- File paths
- Setup instructions

---

## ?? Test Statistics

| Aspect | Value |
|--------|-------|
| **Mermaid Tests** | 10 |
| **PlantUML Tests** | 10 |
| **Total Tests** | 20 |
| **Diagram Types** | 10 (5 Mermaid + 5 PlantUML) |
| **PNG Validations** | 2 (format + content) |
| **Error Cases** | 2 |
| **Diagnostic Tests** | 2 |

---

## ?? Example Output

### Successful PNG Generation
```
Mermaid Status: Mermaid CLI
Is Installed: True
Installed Version: 10.8.0
Installed Path: C:\Users\user\AppData\Roaming\npm\mmdc.cmd

? PNG generated successfully
   Size: 2543 bytes
   Location: C:\Temp\MermaidIntegrationTests_a1b2c3d4\simple.png

? Complex PNG generated successfully
   Size: 5891 bytes
   Location: C:\Temp\MermaidIntegrationTests_a1b2c3d4\complex.png

? Generated: diagram1.png (2543 bytes)
? Generated: diagram2.png (3421 bytes)
? Generated: diagram3.png (4156 bytes)

? Invalid diagram properly rejected (exit code: 1)

? Generated file is a valid PNG
   File size: 2543 bytes
```

### Installation Not Found
```
?? Mermaid CLI is NOT installed
To enable PNG generation tests:
1. Install Node.js from https://nodejs.org/
2. Run: npm install -g @mermaid-js/mermaid-cli
3. Verify with: mmdc --version
```

---

## ?? CI/CD Integration

### Skip in CI (if tools not available)
```bash
dotnet test --filter "Category!=IntegrationTests"
```

### Run in CI (safe to fail gracefully)
```bash
dotnet test --filter "IntegrationTests"
# Tests will skip with AssertInconclusiveException if tools not installed
```

### Run on local dev machine
```bash
# After installing mmdc and/or plantuml.jar
dotnet test --filter "IntegrationTests" --verbosity detailed
```

---

## ?? Documentation Files

1. **UML_INSTALLATION_TESTS.md**
   - Complete test reference
   - All 20 tests documented
   - Troubleshooting guide
   - Prerequisites and setup

2. **RUN_INSTALLATION_TESTS.md**
   - Quick command reference
   - Copy-paste ready commands
   - Installation guides
   - Output interpretation

---

## ? Validation Checklist

- ? 20 integration tests created
- ? Mermaid CLI tested (10 tests)
- ? PlantUML tested (10 tests)
- ? PNG generation validated
- ? PNG file format verified
- ? Error handling tested
- ? Batch processing tested
- ? Auto-cleanup implemented
- ? Graceful skipping for missing tools
- ? Comprehensive documentation
- ? Build successful
- ? CI/CD friendly

---

## ?? What This Solves

**Problem:** "I can't tell if the Mermaid/PlantUML reinstall works"

**Solution:** These tests validate:
1. ? Tool is properly installed
2. ? Tool can be found and executed
3. ? Tool generates PNG files
4. ? PNG files are valid and have content
5. ? Errors are handled gracefully

---

## ?? Next Steps

1. **Run diagnostic tests first:**
   ```bash
   dotnet test FindNeedleUmlDslTests --filter "DiagnosticInfo"
   ```

2. **Install tools if needed:**
   - Follow the instructions from diagnostic output

3. **Run PNG generation tests:**
   ```bash
   dotnet test FindNeedleUmlDslTests --filter "IntegrationTests"
   ```

4. **Check console output for details:**
   - File paths where PNGs were generated
   - File sizes
   - Any errors or warnings

---

**Status:** ? Ready to use - Build successful!
