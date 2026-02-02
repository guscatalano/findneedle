# FindNeedleCoreUtils Testing Guide

This document describes the three complementary testing approaches for `PackagedAppCommandRunner` and `PackagedAppPaths`.

## Overview

Testing command execution in a packaged MSIX app environment is challenging because:
- The packaged app code path (`Invoke-CommandInDesktopPackage`) only works inside an actual MSIX package
- CI/CD pipelines typically run unpackaged
- You need coverage for both packaged and unpackaged scenarios

We use three complementary approaches to achieve comprehensive testing:

---

## Approach 1: Direct Unpackaged Path Testing ?

**Location:** `PackagedAppTests.cs`

Tests the actual command execution in the unpackaged code path.

### Tests
- `RunCommand_Unpackaged_CanRunSimpleCommand()`
- `RunCommandWithOutput_Unpackaged_CapturesOutput()`
- `RunCommand_Unpackaged_TimesOutOnLongRunningCommand()`
- `RunCommand_FailsOnInvalidExecutable()`

### When to Use
- Validating real command execution behavior
- Testing exit codes and output capture
- Testing timeout handling
- CI/CD pipelines (no MSIX required)

### Pros
? Tests actual runtime behavior  
? Works in CI/CD without MSIX  
? No mocking complexity  

### Cons
? Doesn't test packaged app code path  
? Slower (actual command execution)  
? Depends on cmd.exe availability  

### Example
```csharp
[TestMethod]
public void RunCommand_Unpackaged_CanRunSimpleCommand()
{
    var exitCode = PackagedAppCommandRunner.RunCommand(
        "cmd.exe",
        "/c exit 0",
        Path.GetTempPath(),
        5000
    );

    Assert.AreEqual(0, exitCode);
}
```

---

## Approach 2: Helper Method Testing ?

**Location:** `PowerShellCommandBuilderTests.cs`

Tests the command building and PowerShell escaping logic in isolation.

### Tests
- `EscapeForPowerShell_SingleQuotes_AreDoubled()`
- `BuildPathSetupCommand_MultiplePaths_CombinedWithSemicolon()`
- `BuildInnerCommand_IncludesExecutableAndArguments()`
- `BuildPackageContextCommand_ReturnsInvokeCommand()`

### When to Use
- Validating PowerShell escaping logic
- Testing command string construction
- Testing complex string formatting
- Fast unit tests without actual execution

### Pros
? Tests logic without command execution  
? Very fast  
? Easy to test edge cases  
? Implicitly validates packaged path logic  

### Cons
? Doesn't verify commands actually run  
? Only tests logic, not execution  

### Example
```csharp
[TestMethod]
public void EscapeForPowerShell_SingleQuotes_AreDoubled()
{
    var input = "C:\\My'App\\file.txt";
    var result = PowerShellCommandBuilder.EscapeForPowerShell(input);

    Assert.AreEqual("C:\\My''App\\file.txt", result);
}
```

---

## Approach 3: Mock Package Detection ?

**Location:** `PackageContextProviderTests.cs`

Uses abstraction and mocking to simulate packaged vs unpackaged contexts.

### Components
- `IPackageContextProvider` - Interface for package detection
- `ProductionPackageContextProvider` - Real implementation (uses Windows API)
- `TestPackageContextProvider` - Mock for testing
- `PackageContextProviderFactory` - Service locator for switching providers

### Tests
- `TestProvider_SimulatesPackagedApp()`
- `TestProvider_SimulatesUnpackagedApp()`
- `ProviderFactory_ReturnsCurrentProvider()`
- `CommandBuilder_CanBeTestedWithMockedContext()`

### When to Use
- Simulating packaged app context without MSIX
- Testing code paths that check `IsPackagedApp`
- Testing different package family names
- Integration-style testing in unit test framework

### Pros
? Tests packaged app logic without actual MSIX  
? Simulates multiple scenarios  
? Fast (no actual command execution)  
? Full control over package context  

### Cons
?? More setup required  
?? Still doesn't execute `Invoke-CommandInDesktopPackage`  

### Example
```csharp
[TestMethod]
public void CanTestPackagedAppCodePath_WithoutActualMSIX()
{
    // Simulate packaged app context
    var mockProvider = new TestPackageContextProvider(
        isPackagedApp: true, 
        packageFamilyName: "MockedApp_12345"
    );
    PackageContextProviderFactory.SetTestProvider(mockProvider);
    
    // Now test code that checks IsPackagedApp
    var command = PowerShellCommandBuilder.BuildPackageContextCommand(
        packageFamilyName: mockProvider.PackageFamilyName!,
        innerCommand: "Write-Host 'test'"
    );
    
    Assert.IsTrue(command.Contains("MockedApp_12345"));
    
    // Always cleanup
    PackageContextProviderFactory.ResetToProduction();
}
```

---

## Using Approach 3 in Your Code

The abstraction is integrated into the core classes. You can use it like this:

```csharp
// In production, uses real Windows API
var isPackaged = PackagedAppCommandRunner.IsPackagedApp;

// In tests, you can mock it
[TestMethod]
public void MyTest()
{
    PackageContextProviderFactory.SetTestProvider(
        new TestPackageContextProvider(isPackagedApp: true)
    );
    
    // Test code here...
    
    PackageContextProviderFactory.ResetToProduction(); // Cleanup
}
```

---

## Integration with CI/CD

All three approaches work together in a CI/CD pipeline:

1. **Approach 1** runs real command tests (unpackaged path) ?
2. **Approach 2** runs fast logic validation tests ?
3. **Approach 3** runs simulated packaged path tests ?

The result: comprehensive coverage without needing an actual MSIX package!

---

## Comparison Table

| Aspect | Approach 1 | Approach 2 | Approach 3 |
|--------|-----------|-----------|-----------|
| **Tests Real Execution** | ? Yes | ? No | ?? Partial |
| **Tests Packaged Path** | ? No | ? Logic Only | ? Yes |
| **Tests Unpackaged Path** | ? Yes | ? Logic Only | ? Yes |
| **Speed** | Slow | Very Fast | Medium |
| **CI/CD Compatible** | ? Yes | ? Yes | ? Yes |
| **Mocking Required** | ? No | ? No | ? Yes |
| **Setup Complexity** | Simple | Simple | Medium |

---

## Best Practices

1. **Always use Approach 1** for regression testing of real command execution
2. **Use Approach 2** as your primary fast test suite
3. **Use Approach 3** for testing packaged-specific logic paths
4. **Always cleanup** in Approach 3 tests:
   ```csharp
   [TestCleanup]
   public void Cleanup()
   {
       PackageContextProviderFactory.ResetToProduction();
   }
   ```

---

## Related Files

- `PackagedAppCommandRunner.cs` - Main command execution class
- `PackagedAppPaths.cs` - Path utilities for packaged/unpackaged apps
- `PowerShellCommandBuilder.cs` - PowerShell command building helpers
- `PackageContextProvider.cs` - Abstraction for package detection
- `PackagedAppTests.cs` - Approach 1 tests
- `PowerShellCommandBuilderTests.cs` - Approach 2 tests
- `PackageContextProviderTests.cs` - Approach 3 tests
