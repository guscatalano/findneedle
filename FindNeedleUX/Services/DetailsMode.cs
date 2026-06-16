namespace FindNeedleUX.Services;

/// <summary>How the result viewer shows a row's full details.</summary>
public enum DetailsMode
{
    /// <summary>Click a row → it expands inline beneath itself (DataGrid row details).</summary>
    Inrow,

    /// <summary>A persistent, resizable panel docked at the bottom shows the selected row.</summary>
    BottomPanel,

    /// <summary>Double-click a row → a pop-up dialog shows its full details.</summary>
    Popup,
}
