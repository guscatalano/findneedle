using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle.WDK;

namespace FindNeedleUX.Services;
public class SystemInfoMiddleware
{
    public static string GetPanelText()
    {
        return "WDKPath: " + WDKFinder.GetPathOfWDK() + Environment.NewLine +
            "Tracefmt: " + WDKFinder.GetTraceFmtPath();
    }
}
