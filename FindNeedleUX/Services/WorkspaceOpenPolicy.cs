using System;

namespace FindNeedleUX.Services;

/// <summary>What happens when you open another log (file picker, folder picker, "Open with rules",
/// a Recent search, a Known log, or a drag-and-drop) while a workspace is already loaded.
/// Persisted as <see cref="ResultsViewerSettings.OpenIntoWorkspace"/>.</summary>
public enum OpenIntoWorkspaceMode
{
    /// <summary>Add it to the current workspace (default). Sources and rule files are kept.</summary>
    Add,
    /// <summary>Start a new workspace with just the opened log.</summary>
    Replace,
    /// <summary>Ask each time (Add / Replace / Cancel).</summary>
    Ask,
}

/// <summary>
/// The one rule every open path goes through: quick-open must never silently discard a loaded
/// workspace. Pure (no UI, no statics) so it is unit-testable; <c>MainWindow.OpenIntoWorkspaceAsync</c>
/// applies the decision (and shows the Ask dialog).
/// </summary>
public static class WorkspaceOpenPolicy
{
    /// <summary>
    /// Decide how an open should treat the current workspace.
    /// An EMPTY workspace (no sources, no rule files) always just adds — there is nothing to discard, so
    /// the user is never prompted. Otherwise the user's setting wins: Add, Replace, or Ask.
    /// </summary>
    public static OpenIntoWorkspaceMode Decide(bool workspaceEmpty, OpenIntoWorkspaceMode setting)
    {
        if (workspaceEmpty) return OpenIntoWorkspaceMode.Add;
        return setting switch
        {
            OpenIntoWorkspaceMode.Replace => OpenIntoWorkspaceMode.Replace,
            OpenIntoWorkspaceMode.Ask => OpenIntoWorkspaceMode.Ask,
            _ => OpenIntoWorkspaceMode.Add,
        };
    }

    /// <summary>Map the pre-redesign "Drag and drop" setting values (Prompt / ClearAndAdd / AddToExisting)
    /// onto the generalised mode, so an existing choice survives the upgrade. Unknown → null.</summary>
    public static OpenIntoWorkspaceMode? FromLegacyDragDropValue(string legacy)
    {
        if (string.IsNullOrWhiteSpace(legacy)) return null;
        return legacy.Trim().ToLowerInvariant() switch
        {
            "prompt" => OpenIntoWorkspaceMode.Ask,
            "clearandadd" => OpenIntoWorkspaceMode.Replace,
            "addtoexisting" => OpenIntoWorkspaceMode.Add,
            _ => null,
        };
    }

    /// <summary>Parse a persisted value (case-insensitive); null when unrecognised.</summary>
    public static OpenIntoWorkspaceMode? Parse(string value)
        => !string.IsNullOrWhiteSpace(value)
           && Enum.TryParse<OpenIntoWorkspaceMode>(value.Trim(), ignoreCase: true, out var m)
            ? m : null;
}
