using System;
using System.Runtime.InteropServices;
using System.Reflection;
using FindPluginCore.GlobalConfiguration; // Add for settings

namespace FindNeedleUX.Services;
public class SystemInfoMiddleware
{
    public static string GetPanelText()
    {
        string dotnetInfo = $".NET Runtime: {RuntimeInformation.FrameworkDescription}";
        string wdkRootPath = "WDK Root Path: ";
        string tracefmtPath = "Tracefmt: ";
        string defaultViewer = $"Default Result Viewer: {GlobalSettings.DefaultResultViewer}";
        string appVersion = $"App Version: {GetAppVersion()}";
        try
        {
            // Use reflection to load WDKFinder if available
            var wdkType = Type.GetType("findneedle.WDK.WDKFinder, ETWPlugin", throwOnError: false);
            if (wdkType != null)
            {
                // Try to get the WDK root path (private method)
                var getRootMethod = wdkType.GetMethod("GetPathOfWDKRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (getRootMethod != null)
                {
                    var wdkRoot = getRootMethod.Invoke(null, null)?.ToString();
                    wdkRootPath += wdkRoot;
                }
                else
                {
                    wdkRootPath += "Method not found";
                }
                // Get tracefmt path as before
                var getTraceFmtPath = wdkType.GetMethod("GetTraceFmtPath", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (getTraceFmtPath != null)
                {
                    var tracefmt = getTraceFmtPath.Invoke(null, null)?.ToString();
                    tracefmtPath += tracefmt;
                }
                else
                {
                    tracefmtPath += "Method not found";
                }
            }
            else
            {
                wdkRootPath += "WDKFinder not available";
                tracefmtPath += "WDKFinder not available";
            }
        }
        catch (Exception ex)
        {
            wdkRootPath += $"Error: {ex.Message}";
            tracefmtPath += $"Error: {ex.Message}";
        }
        return $"{dotnetInfo}\n{wdkRootPath}\n{tracefmtPath}\n{defaultViewer}\n{appVersion}";
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        return version != null ? version.ToString() : "Unknown";
    }
}
