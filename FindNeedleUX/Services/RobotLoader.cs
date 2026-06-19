using System;
using System.Collections.Generic;
using System.Linq;

namespace FindNeedleUX.Services;

/// <summary>
/// Resolves the animated loader GIFs. Each theme has six ordered frames the loader steps through as a
/// search progresses. Filenames are <c>{theme}_{frame}_{width}.gif</c> (width ∈ 256|wide), except the
/// original "robot" theme whose files carry a fixed <c>_normal_</c> intensity infix
/// (<c>robot_{frame}_normal_{width}.gif</c>). Used by the loading overlay and the settings preview.
/// </summary>
public static class RobotLoader
{
    // Generic step narrative shared by the original themes.
    private static readonly string[] Generic = { "sweep", "scan", "papers", "shelve", "sort", "type" };

    /// <summary>Per-theme ordered frame names (six each), keyed by theme.</summary>
    private static readonly Dictionary<string, string[]> ThemeFrames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["robot"]      = Generic,
        ["arctic"]     = Generic,
        ["forge"]      = Generic,
        ["greenhouse"] = Generic,
        ["haunted"]    = Generic,
        ["library"]    = Generic,
        ["sea"]        = Generic,
        ["zen"]        = new[] { "rake", "stones", "cairn", "prune", "water", "sweep" },
        ["bio"]        = new[] { "swim", "swarm", "branch", "divide", "dna", "heartbeat" },
        ["cosmic"]     = new[] { "comet", "orbit", "constellation", "supernova", "blackhole", "wormhole" },
        ["crystal"]    = new[] { "grow", "lattice", "cascade", "resonate", "spin", "shatter" },
        ["laundromat"] = new[] { "load", "wash", "hang", "iron", "fold", "pair" },
        ["warehouse"]  = new[] { "inventory", "conveyor", "forklift", "stack", "bins", "wrap" },
    };

    /// <summary>Backwards-compatible generic step list (used as a fallback).</summary>
    public static string[] Steps => Generic;

    /// <summary>Animated loader themes (the LoadingAnimation values that are GIF-backed, not Spinner/Bar).</summary>
    public static readonly string[] Themes = ThemeFrames.Keys.ToArray();

    public static bool IsAnimated(string mode) =>
        !string.IsNullOrEmpty(mode) && ThemeFrames.ContainsKey(mode);

    /// <summary>Ordered frame names for a theme (falls back to the generic narrative).</summary>
    public static string[] FramesFor(string theme) =>
        (!string.IsNullOrEmpty(theme) && ThemeFrames.TryGetValue(theme, out var f)) ? f : Generic;

    public static string Uri(int stepIndex, string theme, bool wide)
    {
        var frames = FramesFor(theme);
        var frame = frames[Math.Clamp(stepIndex, 0, frames.Length - 1)];
        var w = wide ? "wide" : "256";
        var t = string.IsNullOrEmpty(theme) ? "robot" : theme.ToLowerInvariant();
        return t == "robot"
            ? $"ms-appx:///Assets/robot_{frame}_normal_{w}.gif"
            : $"ms-appx:///Assets/{t}_{frame}_{w}.gif";
    }

    /// <summary>Resolve using the user's current theme (LoadingAnimation) + width preference.</summary>
    public static string Uri(int stepIndex) =>
        Uri(stepIndex, ResultsViewerSettings.LoadingAnimation, ResultsViewerSettings.RobotWide);

    /// <summary>
    /// Small (64px, no width suffix) loader frame for the inline progressive-loading indicator:
    /// <c>{theme}_{frame}.gif</c>, or <c>robot_{frame}_normal.gif</c> for the robot theme.
    /// </summary>
    public static string SmallUri(int stepIndex, string theme)
    {
        var frames = FramesFor(theme);
        var frame = frames[Math.Clamp(stepIndex, 0, frames.Length - 1)];
        var t = string.IsNullOrEmpty(theme) ? "robot" : theme.ToLowerInvariant();
        return t == "robot"
            ? $"ms-appx:///Assets/robot_{frame}_normal.gif"
            : $"ms-appx:///Assets/{t}_{frame}.gif";
    }

    public static string SmallUri(int stepIndex) => SmallUri(stepIndex, ResultsViewerSettings.LoadingAnimation);
}
