using System;
using System.Runtime.InteropServices;

namespace FindNeedleUX.Services;
public class SystemInfoMiddleware
{
    public static string GetPanelText()
    {
        // Show the .NET runtime version
        return $".NET Runtime: {RuntimeInformation.FrameworkDescription}";
        /*
        return "WDKPath: " + WDKFinder.GetPathOfWDK() + Environment.NewLine +
            "Tracefmt: " + WDKFinder.GetTraceFmtPath();*/
    }
}
