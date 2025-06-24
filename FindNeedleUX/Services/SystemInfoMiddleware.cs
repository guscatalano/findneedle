using System;
using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;
using FindPluginCore.GlobalConfiguration; // Add for settings
using Windows.ApplicationModel; // Correct namespace for Package

namespace FindNeedleUX.Services;
public class SystemInfoMiddleware
{
    public static string GetPanelText()
    {
        string dotnetInfo = $".NET Runtime: {RuntimeInformation.FrameworkDescription}";
        string wdkRootPath = "WDK Root Path: ";
        string tracefmtPath = "Tracefmt: ";
        string defaultViewer = $"Default Result Viewer: {GlobalSettings.DefaultResultViewer}";
        string appVersion, versionSource;
        (appVersion, versionSource) = GetAppVersionAndSource();
        string versionLine = $"App Version: {appVersion}";
        string versionSourceLine = $"Version Source: {versionSource}";
        string buildTimeLine = $"Build Time: {GetBuildTime()}";
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
        return $"{dotnetInfo}\n{wdkRootPath}\n{tracefmtPath}\n{defaultViewer}\n{versionLine}\n{versionSourceLine}\n{buildTimeLine}";
    }

    private static (string version, string source) GetAppVersionAndSource()
    {
        try
        {
            var package = Package.Current;
            var v = package.Id.Version;
            return ($"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}", "Package");
        }
        catch
        {
            // Fallback to assembly version if not running as packaged app
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return (version != null ? version.ToString() : "Unknown", "Assembly");
        }
    }

    private static string GetBuildTime()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var filePath = assembly.Location;
            var lastWrite = File.GetLastWriteTime(filePath);
            return lastWrite.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch
        {
            return "Unknown";
        }
    }
}
