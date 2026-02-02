# TestUtilities

Shared test utilities and helpers for performance testing and system requirement validation across all test projects.

## Overview

TestUtilities is a centralized .NET 8 library providing reusable infrastructure for:
- **Performance testing** - Measuring and validating test performance
- **System requirements** - Automatically skipping tests on machines that don't meet minimum specs
- **CI/CD integration** - Graceful handling of different test environments

## Project Structure

```
TestUtilities/
??? TestUtilities.csproj
??? README.md
??? Helpers/
    ??? SystemSpecificationChecker.cs
    ??? PerformanceTestInitializer.cs
```

## Components

### SystemSpecificationChecker

Checks if the current machine meets minimum performance test requirements.

**Key Methods:**
- `GetProcessorCount()` - Returns CPU core count
- `GetTotalMemoryGb()` - Returns available RAM in GB
- `MeetsMinimumRequirements()` - Checks if system meets defaults (4GB RAM, 2 cores)
- `GetSystemSpecificationSummary()` - Returns diagnostic message about system specs
- `SkipIfInsufficientSpecs()` - Returns skip message if requirements not met

**Default Requirements:**
- Minimum RAM: 4 GB
- Minimum Processors: 2 cores

**Usage Example:**

```csharp
using TestUtilities.Helpers;

// Check if system meets minimum specs
if (SystemSpecificationChecker.MeetsMinimumRequirements())
{
    Console.WriteLine(SystemSpecificationChecker.GetSystemSpecificationSummary());
}

// Get individual specs
int cores = SystemSpecificationChecker.GetProcessorCount();
double ramGb = SystemSpecificationChecker.GetTotalMemoryGb();
```

### RequiresMinimumSpecsAttribute

Custom attribute to mark tests that require specific system specifications.

**Attribute Parameters:**
- `MinimumRamGb` (int) - Minimum RAM in GB. Set to -1 to use default (4 GB)
- `MinimumProcessorCount` (int) - Minimum processor count. Set to -1 to use default (2 cores)
- `Reason` (string) - Optional message explaining why these specs are required

### PerformanceTestInitializer

Helper class to automatically check system requirements during test initialization.

**Key Method:**
- `CheckSystemRequirements(TestContext)` - Call from `[TestInitialize]` to validate specs

**Behavior:**
When a test with `[RequiresMinimumSpecs]` runs on a machine that doesn't meet requirements:
- The test is marked as `Inconclusive`
- A diagnostic message is displayed showing:
  - Current system specs (CPU cores, RAM)
  - Required specs
  - Custom reason message

Example diagnostic output:
```
Skipped: Stress test with large dataset. System has 1 CPU cores and 2.00 GB RAM. 
Test requires 2 cores and 4 GB.
```

## Quick Start

### Basic Test Class Setup

```csharp
using TestUtilities.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class MyPerformanceTests
{
    private TestContext? _testContext;

    public TestContext TestContext
    {
        get => _testContext ?? throw new InvalidOperationException("TestContext not initialized");
        set => _testContext = value;
    }

    [TestInitialize]
    public void TestInitialize()
    {
        // Check system requirements for all tests in this class
        PerformanceTestInitializer.CheckSystemRequirements(TestContext);
    }

    // Standard test - runs on any machine
    [TestMethod]
    public void BasicTest()
    {
        // Your test code
    }

    // Performance test - skips on small VMs
    [TestMethod]
    [RequiresMinimumSpecs(MinimumRamGb = 4, MinimumProcessorCount = 2, 
        Reason = "Stress test with large dataset")]
    public void StressTest()
    {
        // Your test code
    }

    // Custom specs for high-demand tests
    [TestMethod]
    [RequiresMinimumSpecs(MinimumRamGb = 8, MinimumProcessorCount = 4,
        Reason = "Concurrent load testing with 10 parallel threads")]
    public void ConcurrentLoadTest()
    {
        // Your test code
    }
}
```

## Integration with Test Projects

### Adding TestUtilities to a New Test Project

1. **Add project reference** to your test project's `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\TestUtilities\TestUtilities.csproj" />
</ItemGroup>
```

2. **Add using statement:**

```csharp
using TestUtilities.Helpers;
```

3. **Implement TestContext** property and call `CheckSystemRequirements`:

```csharp
[TestClass]
public class MyTests
{
    private TestContext? _testContext;

    public TestContext TestContext
    {
        get => _testContext ?? throw new InvalidOperationException("TestContext not initialized");
        set => _testContext = value;
    }

    [TestInitialize]
    public void TestInitialize()
    {
        PerformanceTestInitializer.CheckSystemRequirements(TestContext);
    }
}
```

## Projects Currently Using TestUtilities

- **PerformanceTests** - Adaptive performance tests for storage implementations with 2M record stress tests
- **ETWPluginTests** - ETW/ETL processing performance tests for large file handling

## Running Tests with Filters

### Skip performance tests on small machines:
```bash
dotnet test --filter "TestCategory!=Performance"
```

### Run only performance tests:
```bash
dotnet test --filter "TestCategory=Performance"
```

### Run specific test project:
```bash
dotnet test PerformanceTests
```

## Best Practices

1. **Mark tests appropriately** - Only add `[RequiresMinimumSpecs]` to tests that truly require high performance specs
2. **Provide clear reasons** - Always include a `Reason` parameter explaining why specs are needed
3. **Be conservative with requirements** - Set minimum specs to what's truly necessary
4. **Document in comments** - Add comments in tests explaining performance-intensive operations
5. **Use consistent configuration** - Don't duplicate requirement checks across multiple tests

Example of well-documented test:

```csharp
[TestMethod]
[RequiresMinimumSpecs(MinimumRamGb = 8, MinimumProcessorCount = 4,
    Reason = "Concurrent load test with 10 parallel threads processing 1M records")]
public void ParallelLoadTest()
{
    // This test spins up 10 parallel threads, each processing 100K records
    // Higher specs prevent thread context switching overhead from skewing results
}
```

## Benefits

? **Centralized test infrastructure** - Single source of truth for performance test utilities  
? **Reusable across projects** - Any test project can reference TestUtilities  
? **Flexible specs** - Each test can have custom requirements  
? **Clear diagnostics** - Tests report why they were skipped and system specs  
? **CI/CD friendly** - Works with test filters and parallel execution  
? **No code duplication** - Shared helpers eliminate duplicate code across test projects  

## Extending TestUtilities

To add new capabilities:

1. Create new helpers in `TestUtilities/Helpers/`
2. Add public methods and classes as needed
3. Update this README with usage documentation
4. Update all referencing projects' `.csproj` files if dependencies change

Example structure for new helper:

```csharp
namespace TestUtilities.Helpers;

/// <summary>
/// Your helper description
/// </summary>
public class MyNewHelper
{
    public static void MyMethod()
    {
        // Implementation
    }
}
```

## Build Status

? Build successful - All projects compiling correctly  
? All test projects integrated and validated

