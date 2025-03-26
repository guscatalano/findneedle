using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindPluginCore.GlobalConfiguration;

public class GlobalSettings
{
    //Enable debug mode (noisier output)
    private static bool _debug = false;
    public static bool Debug
    {
        get => _debug;
        set => _debug = value;
    }
}
