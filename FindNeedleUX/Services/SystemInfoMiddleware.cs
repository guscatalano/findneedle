using System;
using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;
using FindPluginCore.GlobalConfiguration; // Add for settings
using Windows.ApplicationModel; // Correct namespace for Package
using findneedle.PluginSubsystem; // For PluginManager
using Microsoft.Win32;

namespace FindNeedleUX.Services;
public class SystemInfoMiddleware
{
    public static string StoreUrl => "https://www.microsoft.com/store/productId/9NWLTBV4NRDL?ocid=libraryshare";
    public static string MsStoreUrl => "ms-windows-store://pdp/?productid=9NWLTBV4NRDL";
    public static string GithubReleasesUrl => "https://github.com/guscatalano/findneedle/releases";
    public static string GithubUrl => "https://github.com/guscatalano/findneedle";

    public static string GetPanelText()
    {
        var dotnetInfo = $".NET Runtime: {RuntimeInformation.FrameworkDescription}";
        var osVersion = $"Windows Version: {GetWindowsVersion()}";
        var wdkRootPath = "WDK Root Path: ";
        var tracefmtPath = "Tracefmt: ";
        var defaultViewer = $"Default Result Viewer: {GlobalSettings.DefaultResultViewer}";
        var plantumlPath = $"PlantUML Path: {GetPlantUMLPath()}";
        string appVersion, versionSource;
        (appVersion, versionSource) = GetAppVersionAndSource();
        var versionLine = $"App Version: {appVersion}";
        var versionSourceLine = $"Version Source: {versionSource}";
        var buildTimeLine = $"Build Time: {GetBuildTime()}";
        var msStoreVersionLine = $"MS-Store Version: {GetMsStoreVersion()}";
        var storeLine = $"Store Page: {StoreUrl}";
        var msStoreLine = $"MS-Store Link: {MsStoreUrl}";
        var githubReleasesLine = $"GitHub Releases: {GithubReleasesUrl}";
        var githubLine = $"GitHub: {GithubUrl}";
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
        return $"{dotnetInfo}\n{osVersion}\n{wdkRootPath}\n{tracefmtPath}\n{defaultViewer}\n{plantumlPath}\n{versionLine}\n{versionSourceLine}\n{msStoreVersionLine}\n{buildTimeLine}\n{storeLine}\n{msStoreLine}\n{githubReleasesLine}\n{githubLine}";
    }

    public static string GetPlantUMLPath()
    {
        var mgr = PluginManager.GetSingleton();
        var val = mgr.config?.PlantUMLPath ?? string.Empty;
        if (val.StartsWith("reg:", StringComparison.OrdinalIgnoreCase))
        {
            // Format: reg:HIVE\\KeyPath[\\ValueName]
            // Example: reg:HKEY_CURRENT_USER\\Software\\FindNeedle\\PlantUMLPath
            var regPath = val.Substring(4);
            string hive = "";
            string keyPath = regPath;
            string valueName = "";
            int hiveSep = regPath.IndexOf("\\");
            if (hiveSep > 0)
            {
                hive = regPath.Substring(0, hiveSep);
                keyPath = regPath.Substring(hiveSep + 1);
            }
            int valueSep = keyPath.LastIndexOf(":");
            if (valueSep > 0)
            {
                valueName = keyPath.Substring(valueSep + 1);
                keyPath = keyPath.Substring(0, valueSep);
            }
            RegistryKey baseKey = hive.ToUpper() switch
            {
                "HKEY_CURRENT_USER" => Registry.CurrentUser,
                "HKCU" => Registry.CurrentUser,
                "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                "HKLM" => Registry.LocalMachine,
                _ => Registry.CurrentUser
            };
            try
            {
                using var regKey = baseKey.OpenSubKey(keyPath);
                if (regKey != null)
                {
                    var regVal = regKey.GetValue(string.IsNullOrEmpty(valueName) ? null : valueName) as string;
                    if (!string.IsNullOrWhiteSpace(regVal))
                        return regVal;
                }
            }
            catch { }
            return string.Empty;
        }
        return val;
    }

    public static void SetPlantUMLPath(string newPath)
    {
        var mgr = PluginManager.GetSingleton();
        if (mgr.config != null)
        {
            mgr.config.PlantUMLPath = newPath;
            mgr.SaveToFile();
        }
    }

    private static string GetWindowsVersion()
    {
        try
        {
            var os = Environment.OSVersion;
            return $"{os.VersionString}";
        }
        catch
        {
            return "Unknown";
        }
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

    private static string GetMsStoreVersion()
    {
        try
        {
            var v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
        }
        catch
        {
            return "N/A";
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
