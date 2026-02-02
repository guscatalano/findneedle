using System;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestUtilities.Helpers;

/// <summary>
/// Attribute to mark tests that require minimum system specifications.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class RequiresMinimumSpecsAttribute : Attribute
{
    /// <summary>
    /// Custom reason message for why these specs are required.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Minimum RAM in GB. Set to -1 to use default.
    /// </summary>
    public int MinimumRamGb { get; set; } = -1;

    /// <summary>
    /// Minimum processor count. Set to -1 to use default.
    /// </summary>
    public int MinimumProcessorCount { get; set; } = -1;

    public RequiresMinimumSpecsAttribute()
    {
    }

    public RequiresMinimumSpecsAttribute(string reason)
    {
        Reason = reason;
    }
}

/// <summary>
/// Helper to manage test initialization with spec checking.
/// </summary>
public class PerformanceTestInitializer
{
    /// <summary>
    /// Call this from [TestInitialize] to automatically skip tests that don't meet specs.
    /// </summary>
    public static void CheckSystemRequirements(TestContext testContext)
    {
        var testName = testContext.TestName;
        if (string.IsNullOrEmpty(testName))
            return;

        // Extract method name from full test name (format: Namespace.ClassName.MethodName)
        var testNameParts = testName.Split('.');
        if (testNameParts.Length < 2)
            return;

        var methodName = testNameParts[testNameParts.Length - 1];
        var className = testNameParts.Length > 1 ? testNameParts[testNameParts.Length - 2] : null;

        if (className == null)
            return;

        // Get the test class type from the calling assembly
        var assembly = Assembly.GetCallingAssembly();
        var testType = assembly.GetTypes()
            .FirstOrDefault(t => t.Name == className);

        if (testType == null)
            return;

        var testMethod = testType.GetMethod(methodName, 
            BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

        if (testMethod == null)
            return;

        var attr = testMethod.GetCustomAttribute<RequiresMinimumSpecsAttribute>();
        if (attr == null)
            return;

        var processorCount = Environment.ProcessorCount;
        var ramGb = GC.GetTotalMemory(false) / (1024.0 * 1024.0 * 1024.0);

        var minRam = attr.MinimumRamGb >= 0 ? attr.MinimumRamGb : SystemSpecificationChecker.MinimumRamGb;
        var minProcessors = attr.MinimumProcessorCount >= 0 ? attr.MinimumProcessorCount : SystemSpecificationChecker.MinimumProcessorCount;

        if (processorCount < minProcessors || ramGb < minRam)
        {
            var reason = attr.Reason ?? "insufficient system specifications";
            Assert.Inconclusive(
                $"Skipped: {reason}. System has {processorCount} CPU cores and {ramGb:F2} GB RAM. " +
                $"Test requires {minProcessors} cores and {minRam} GB."
            );
        }
    }
}
