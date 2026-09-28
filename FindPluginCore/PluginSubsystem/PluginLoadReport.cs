using System;
using System.Collections.Generic;
using System.Linq;

namespace findneedle.PluginSubsystem;

/// <summary>One plugin that did not load, and why.</summary>
public sealed record PluginLoadFailure(string Plugin, string Reason);

/// <summary>
/// What happened the last time plugins were loaded - so a failure can be SAID rather than inferred.
///
/// This exists because of a shipped bug: the published app had no FakeLoadPlugin.exe, every plugin
/// threw, no file-extension processor registered, and the viewer reported "This search returned no
/// rows - the source was empty, or nothing matched/decoded". The app blamed the user's log for its
/// own missing file, and the only trace was a line in a log nobody reads. A load failure is not an
/// empty search result, and the difference has to reach the screen.
/// </summary>
public static class PluginLoadReport
{
    private static readonly object Gate = new();
    private static readonly List<PluginLoadFailure> Failures = new();
    private static int _loadedModules;

    /// <summary>Forget the previous load's outcome (called as a load starts).</summary>
    public static void Reset()
    {
        lock (Gate) { Failures.Clear(); _loadedModules = 0; }
    }

    public static void RecordFailure(string plugin, string reason)
    {
        if (string.IsNullOrWhiteSpace(plugin)) return;
        lock (Gate) Failures.Add(new PluginLoadFailure(plugin, reason ?? ""));
    }

    public static void RecordLoaded() { lock (Gate) _loadedModules++; }

    public static IReadOnlyList<PluginLoadFailure> GetFailures()
    {
        lock (Gate) return Failures.ToList();
    }

    public static int LoadedModuleCount { get { lock (Gate) return _loadedModules; } }

    /// <summary>
    /// A one-line headline for the state of plugin loading, or null when there is nothing to say.
    /// Pure given its inputs so the wording is testable without a plugin subsystem.
    /// </summary>
    public static string Headline(int loadedModules, IReadOnlyList<PluginLoadFailure> failures, int extensionProcessors)
    {
        failures ??= Array.Empty<PluginLoadFailure>();

        // Nothing at all can be read: the app cannot do its job, and no log is to blame.
        if (extensionProcessors == 0 && (failures.Count > 0 || loadedModules == 0))
        {
            var missingLoader = failures.Any(f => f.Reason.IndexOf("fake loader", StringComparison.OrdinalIgnoreCase) >= 0);
            return missingLoader
                ? "No log parsers loaded - this build is missing its plugin loader (FakeLoadPlugin.exe)"
                : "No log parsers loaded - every plugin failed to load";
        }

        // Some loaded, some did not: file types are missing but the app still works.
        if (failures.Count > 0)
            return failures.Count == 1
                ? $"1 plugin failed to load ({failures[0].Plugin}) - some file types may not be readable"
                : $"{failures.Count} plugins failed to load - some file types may not be readable";

        return null;
    }

    /// <summary>The headline for the CURRENT state, given how many extension processors were found.</summary>
    public static string HeadlineFor(int extensionProcessors)
        => Headline(LoadedModuleCount, GetFailures(), extensionProcessors);

    /// <summary>Every failure, one per line - for the detail pane / copy button.</summary>
    public static string Detail()
    {
        var failures = GetFailures();
        if (failures.Count == 0) return "";
        return string.Join(Environment.NewLine, failures.Select(f => $"{f.Plugin}: {f.Reason}"));
    }
}
