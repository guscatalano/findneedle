using System;
using System.Runtime.InteropServices;

namespace TestUtilities.Helpers;

/// <summary>
/// Checks if the current machine meets minimum performance test requirements.
/// </summary>
public class SystemSpecificationChecker
{
    /// <summary>
    /// Minimum RAM required for performance tests (in GB).
    /// </summary>
    public const int MinimumRamGb = 4;

    /// <summary>
    /// Minimum processor count for performance tests.
    /// </summary>
    public const int MinimumProcessorCount = 2;

    /// <summary>
    /// Gets the total physical memory available to the runtime, in GB.
    /// Uses <see cref="GC.GetGCMemoryInfo()"/>.<c>TotalAvailableMemoryBytes</c>, which reports the
    /// machine's physical RAM (or the container/cgroup limit when constrained) — the thing we
    /// actually want to gate on. The previous implementation used <c>GC.GetTotalMemory()</c>, the
    /// managed heap size (a few tens of MB), so this check always saw ~0 GB and could never be met.
    /// </summary>
    public static double GetTotalMemoryGb()
    {
        var totalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return totalBytes / (1024.0 * 1024.0 * 1024.0);
    }

    /// <summary>
    /// Gets the number of processor cores.
    /// </summary>
    public static int GetProcessorCount()
    {
        return Environment.ProcessorCount;
    }

    /// <summary>
    /// Checks if the machine meets minimum requirements for performance tests.
    /// </summary>
    public static bool MeetsMinimumRequirements()
    {
        return GetProcessorCount() >= MinimumProcessorCount && GetTotalMemoryGb() >= MinimumRamGb;
    }

    /// <summary>
    /// Gets a diagnostic message about system specs.
    /// </summary>
    public static string GetSystemSpecificationSummary()
    {
        var processorCount = GetProcessorCount();
        var memoryGb = GetTotalMemoryGb();

        return $"System: {processorCount} CPU cores, {memoryGb:F2} GB RAM " +
               $"(Min required: {MinimumProcessorCount} cores, {MinimumRamGb} GB)";
    }

    /// <summary>
    /// Skips a test if system doesn't meet requirements, returning a diagnostic message.
    /// </summary>
    public static string SkipIfInsufficientSpecs()
    {
        if (!MeetsMinimumRequirements())
        {
            return $"Skipped: {GetSystemSpecificationSummary()}";
        }
        return string.Empty;
    }

    /// <summary>
    /// Checks if the current platform is Windows.
    /// </summary>
    public static bool IsWindows()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }

    /// <summary>
    /// Checks if the current platform is Linux.
    /// </summary>
    public static bool IsLinux()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    }

    /// <summary>
    /// Checks if the current platform is macOS.
    /// </summary>
    public static bool IsMacOS()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }
}
