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

    // Default result viewer setting
    private static string _defaultResultViewer = "resultswebpage";
    public static string DefaultResultViewer
    {
        get => _defaultResultViewer;
        set => _defaultResultViewer = value?.ToLower() ?? "resultswebpage";
    }

    // Toggle the debug flag
    public static void ToggleDebug()
    {
        _debug = !_debug;
    }
}
