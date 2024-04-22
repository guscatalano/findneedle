using System;
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
