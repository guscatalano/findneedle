using System;
using System.Runtime.InteropServices;

namespace FindNeedleUX.Services;
public class SystemInfoMiddleware
{
    public static string GetPanelText()
    {
        string dotnetInfo = $".NET Runtime: {RuntimeInformation.FrameworkDescription}";
        string wdkPath = "WDKPath: ";
        string tracefmtPath = "Tracefmt: ";
        try
        {
            // Use reflection to load WDKFinder if available
            var wdkType = Type.GetType("findneedle.WDK.WDKFinder, ETWPlugin", throwOnError: false);
            if (wdkType != null)
            {
                var getTraceFmtPath = wdkType.GetMethod("GetTraceFmtPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (getTraceFmtPath != null)
                {
                    var tracefmt = getTraceFmtPath.Invoke(null, null)?.ToString();
                    wdkPath += tracefmt;
                    tracefmtPath += tracefmt;
                }
                else
                {
                    wdkPath += "Method not found";
                    tracefmtPath += "Method not found";
                }
            }
            else
            {
                wdkPath += "WDKFinder not available";
                tracefmtPath += "WDKFinder not available";
            }
        }
        catch (Exception ex)
        {
            wdkPath += $"Error: {ex.Message}";
            tracefmtPath += $"Error: {ex.Message}";
        }
        return $"{dotnetInfo}\n{wdkPath}\n{tracefmtPath}";
    }
}
