# Enhanced Installation Tests - Summary

## ? What's New

The Mermaid installation tests have been significantly enhanced to test the **actual installation process**, not just check if the tools are already installed.

---

## ?? Two Testing Scenarios

### **1. System-Wide Installation Tests** (Existing Tools)
Tests using Mermaid CLI already installed in system PATH.

- ? `MermaidInstaller_SystemWide_ChecksStatus()` - Detect system installation
- ? `MermaidInstaller_SystemWide_CanFindMermaidCli()` - Verify mmdc executable
- ? `MermaidInstaller_SystemWide_CanGeneratePng_SimpleFlowchart()` - PNG generation
- ? `MermaidInstaller_CanGeneratePng_ComplexDiagram()` - Complex diagrams
- ? `MermaidInstaller_CanGeneratePng_ClassDiagram()` - Class diagrams
- ? `MermaidInstaller_CanGenerateMultiplePngs()` - Batch generation
- ? `MermaidInstaller_InvalidDiagram_FailsGracefully()` - Error handling
- ? `MermaidInstaller_GeneratedPngIsValidImage()` - PNG validation
- ? `MermaidInstaller_DiagnosticInfo()` - Installation diagnostics

### **2. Custom Installation Tests** (Installation Process)
Tests that **actually install** Mermaid to a custom directory, then verify functionality.

- ? `MermaidInstaller_CustomDirectory_CanInstall()` - **Actually runs installation**
  - Creates temp directory
  - Calls `InstallAsync()` method
  - Verifies mmdc executable is created
  - Reports progress during installation

- ? `MermaidInstaller_CustomDirectory_CanGeneratePng_AfterInstall()` - **Tests PNG generation after install**
  - Installs Mermaid
  - Uses the freshly installed mmdc
  - Generates PNG
  - Validates output

- ? `MermaidInstaller_CustomDirectory_StatusReflectsNotInstalled()` - Status tracking
  - Verifies status before installation
  - Shows installation is detected after

---

## ?? What This Tests

| Test | Tests What | Benefits |
|------|-----------|----------|
| System tests | System-wide installation | Validates existing setup works |
| Custom install test | Installation process | **Tests the actual installer code** |
| Post-install PNG test | Fresh installation | **End-to-end verification** |
| Status tests | Detection logic | Ensures status API is accurate |

---

## ?? How Custom Installation Test Works

```
CustomDirectory_CanInstall()
  ?
1. Create temp directory
  ?
2. new MermaidInstaller(customDir)
  ?
3. InstallAsync() 
  ?? Downloads Node.js (if on Windows)
  ?? Extracts Node
  ?? Runs npm install
  ?? Reports progress
  ?? Returns mmdc path
  ?
4. Assert mmdc executable exists
  ?
5. Return success ?

Then CustomDirectory_CanGeneratePng_AfterInstall()
  ?
1. Use installed mmdc from above
  ?
2. Create test diagram
  ?
3. Run mmdc with freshly installed executable
  ?
4. Verify PNG generated
  ?
5. Return success ?
```

---

## ?? Key Features

### **Installation Testing**
- ? Actually calls `InstallAsync()` method
- ? Validates entire installation pipeline
- ? Tests on Windows (downloads Node.js)
- ? Reports progress to console
- ? Handles custom directories

### **End-to-End Testing**
- ? Installation ? Generation workflow
- ? Uses installed executables (not system PATH)
- ? Validates fresh installation works
- ? Real PNG generation from installed tool

### **Progress Reporting**
```csharp
var progress = new Progress<InstallProgress>(p =>
{
    Console.WriteLine($"  [{p.PercentComplete}%] {p.Status}");
});

var result = await _customInstaller.InstallAsync(progress, CancellationToken.None);
```

Output:
```
Installing Mermaid to: C:\Temp\MermaidInstall_abc123
  [5%] Downloading portable Node runtime...
  [10%] Extracting Node...
  [20%] Running npm install...
  [50%] npm install --prefix "..." @mermaid-js/mermaid-cli
  [100%] mermaid-cli installed
? Installation successful
   mmdc path: C:\Temp\MermaidInstall_abc123\node_modules\.bin\mmdc.cmd
```

---

## ?? Running the Tests

```bash
# Run all Mermaid tests (system + custom installation)
dotnet test FindNeedleToolInstallerTests --filter "MermaidInstallationIntegrationTests"

# Run only system-wide tests (fast)
dotnet test FindNeedleToolInstallerTests --filter "SystemWide"

# Run only custom installation tests (slower, ~2 minutes)
dotnet test FindNeedleToolInstallerTests --filter "CustomDirectory" --verbosity detailed

# Run single test with detailed output
dotnet test FindNeedleToolInstallerTests --filter "CustomDirectory_CanInstall" --verbosity detailed
```

---

## ?? Timing

| Test | Time | Notes |
|------|------|-------|
| System-wide tests | ~1-2 sec | Uses existing installation |
| Custom install test | ~30-60 sec | Downloads Node.js (first time) |
| Post-install PNG test | ~5-10 sec | Uses installed mmdc |
| **Total suite** | ~2 minutes | On clean system |

---

## ??? Error Handling

- ? `AssertInconclusiveException` if tools not found (skips gracefully)
- ? `InstallResult.Failed()` if installation fails (shows error message)
- ? `RunMermaidCommandWithPath()` validates custom mmdc path
- ? PNG header validation ensures valid output

---

## ?? Example Output

### Success
```
Installing Mermaid to: C:\Users\user\AppData\Local\Temp\MermaidInstall_xyz
? Installation successful
   mmdc path: C:\Users\user\AppData\Local\Temp\MermaidInstall_xyz\node_modules\.bin\mmdc.cmd

? PNG generated after custom installation
   Size: 2543 bytes
```

### Skip (Tool not available)
```
AssertInconclusiveException: Mermaid CLI is not installed in system PATH
```

### Failure
```
AssertionError: Installation failed: npm install failed (system): ...
```

---

## ? Benefits

1. **Tests Real Installation** - Actually runs the installation process
2. **End-to-End Validation** - Installation + PNG generation workflow
3. **Custom Directories** - Tests isolated installation in temp directory
4. **Progress Reporting** - Visibility into what installer is doing
5. **Windows Support** - Tests portable Node.js download/extraction
6. **Graceful Skipping** - System tests skip if tools not installed
7. **Comprehensive** - Tests both system-wide and fresh installations

---

## ?? Next Steps

1. **Run system-wide tests first** (fast):
   ```bash
   dotnet test FindNeedleToolInstallerTests --filter "SystemWide"
   ```

2. **Run custom installation tests** (if you want to validate installer):
   ```bash
   dotnet test FindNeedleToolInstallerTests --filter "CustomDirectory"
   ```

3. **Same pattern for PlantUML** - Can be enhanced similarly

---

**Status:** ? Enhanced and tested - Build successful!
