using System;

namespace FindNeedleUX.Services;

/// <summary>
/// Resolves the animated "robot" loader GIFs. Assets follow the naming
/// <c>robot_{step}_{intensity}_{width}.gif</c> where step ∈ Steps, intensity ∈ busy|normal|calm,
/// width ∈ 256|wide. Used by the loading overlay and the settings preview.
/// </summary>
public static class RobotLoader
{
    /// <summary>Ordered narrative the loader steps through as a search progresses.</summary>
    public static readonly string[] Steps = { "sweep", "scan", "papers", "shelve", "sort", "type" };

    public static string Uri(int stepIndex, string intensity, bool wide)
    {
        var step = Steps[Math.Clamp(stepIndex, 0, Steps.Length - 1)];
        var i = (intensity ?? "normal").ToLowerInvariant();
        if (i != "busy" && i != "calm") i = "normal";
        var w = wide ? "wide" : "256";
        return $"ms-appx:///Assets/robot_{step}_{i}_{w}.gif";
    }

    /// <summary>Resolve using the user's current intensity/width preferences.</summary>
    public static string Uri(int stepIndex) =>
        Uri(stepIndex, ResultsViewerSettings.RobotIntensity, ResultsViewerSettings.RobotWide);
}
