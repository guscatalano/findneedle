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
    /// Gets the total physical memory in GB.
    /// </summary>
    public static double GetTotalMemoryGb()
    {
        var ram = GC.GetTotalMemory(false);
        return ram / (1024.0 * 1024.0 * 1024.0);
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
        var memoryGb = GC.GetTotalMemory(false) / (1024.0 * 1024.0 * 1024.0);
        
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
