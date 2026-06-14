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
        var methodName = testContext.TestName;
        if (string.IsNullOrEmpty(methodName))
            return;
        // TestName is normally just the method name, but tolerate a fully-qualified form too.
        methodName = methodName.Split('.').Last();

        // Resolve the declaring class. MSTest exposes it via FullyQualifiedTestClassName; the old
        // code instead parsed it out of TestName and bailed (Length < 2) whenever TestName was the
        // bare method name — i.e. the normal case — so this whole spec check silently no-opped and
        // the [RequiresMinimumSpecs] gate never ran.
        var className = testContext.FullyQualifiedTestClassName;

        var assembly = Assembly.GetCallingAssembly();
        Type? testType = null;
        if (!string.IsNullOrEmpty(className))
            testType = assembly.GetTypes().FirstOrDefault(t => t.FullName == className || t.Name == className);

        var testMethod = testType?.GetMethod(methodName,
            BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

        // Fallback: if the class couldn't be pinned, find any method with this name that actually
        // carries the attribute (keeps the gate working if a test is renamed/relocated).
        if (testMethod == null)
        {
            foreach (var t in assembly.GetTypes())
            {
                var m = t.GetMethod(methodName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
                if (m != null && m.GetCustomAttribute<RequiresMinimumSpecsAttribute>() != null)
                {
                    testMethod = m;
                    break;
                }
            }
        }
        if (testMethod == null)
            return;

        var attr = testMethod.GetCustomAttribute<RequiresMinimumSpecsAttribute>();
        if (attr == null)
            return;

        var processorCount = Environment.ProcessorCount;
        var ramGb = SystemSpecificationChecker.GetTotalMemoryGb();

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
