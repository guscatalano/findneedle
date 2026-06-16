using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindPluginCore.GlobalConfiguration;

[ExcludeFromCodeCoverage]
public class GlobalSettings
{
    //Enable debug mode (noisier output)
    private static bool _debug = false;
    public static bool Debug
    {
        get => _debug;
        set => _debug = value;
    }

    // Result viewer configuration. (The web/DataTables viewer was removed; native is the only viewer.)
    public const string NativeResultViewerKey = "nativereviewer";

    private static string _defaultResultViewer = NativeResultViewerKey;

    /// <summary>
    /// Legacy in-memory-only default-viewer setting. The persisted value lives on
    /// <c>FindNeedleUX.Services.ResultsViewerSettings.DefaultResultViewer</c> (saved to
    /// <c>viewer-settings.json</c>); this field is kept for any external integration that
    /// may still reference it but is no longer read by FindNeedleUX itself.
    /// </summary>
    [Obsolete("Use FindNeedleUX.Services.ResultsViewerSettings.DefaultResultViewer instead — it persists across restarts.")]
    public static string DefaultResultViewer
    {
        get => _defaultResultViewer;
        set => _defaultResultViewer = value?.ToLower() ?? NativeResultViewerKey;
    }

    // Toggle the debug flag
    public static void ToggleDebug()
    {
        _debug = !_debug;
    }
}
