using System.Collections.Generic;
using System.Linq;

namespace CoreTests.Helpers;

/// <summary>
/// Helper methods for statistical calculations in performance tests.
/// </summary>
public static class StatisticsHelper
{
    /// <summary>
    /// Calculates the median value from a list of doubles.
    /// </summary>
    public static double GetMedian(List<double> values)
    {
        if (values == null || values.Count == 0)
            return 0;

        var sorted = values.OrderBy(x => x).ToList();
        int n = sorted.Count;
        
        if (n % 2 == 1)
            return sorted[n / 2];
        
        return (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
    }

    /// <summary>
    /// Calculates baseline (average of first N values).
    /// </summary>
    public static double GetBaseline(List<double> values, int count = 10)
    {
        if (values == null || values.Count == 0)
            return 0;

        return values.Take(System.Math.Min(count, values.Count)).Average();
    }
}
