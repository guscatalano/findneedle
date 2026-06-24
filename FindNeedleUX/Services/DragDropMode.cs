namespace FindNeedleUX.Services;

/// <summary>What happens when you drag a file/folder onto the viewer while a workspace is already
/// loaded.</summary>
public enum DragDropMode
{
    /// <summary>Ask each time (default).</summary>
    Prompt,
    /// <summary>Clear the current workspace, then open the dropped file(s).</summary>
    ClearAndAdd,
    /// <summary>Add the dropped file(s) to the existing workspace.</summary>
    AddToExisting,
}
