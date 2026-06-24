using System;
using System.Runtime.InteropServices;
using System.Reflection;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FindPluginCore.GlobalConfiguration; // Add for settings
using Windows.ApplicationModel; // Correct namespace for Package
using findneedle.PluginSubsystem; // For PluginManager
using Microsoft.Win32;
using FindNeedleToolInstallers;
using FindNeedleCoreUtils;

namespace FindNeedleUX.Services;
public class SystemInfoMiddleware
{
    private static readonly Lazy<UmlDependencyManager> _umlDependencyManager = new(() => new UmlDependencyManager());
    
    /// <summary>
    /// Gets the singleton UML dependency manager instance.
    /// </summary>
    public static UmlDependencyManager UmlDependencyManager => _umlDependencyManager.Value;
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
        var defaultViewer = $"Default Result Viewer: {FindNeedleUX.Services.ResultsViewerSettings.DefaultResultViewer}";
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

    /// <summary>One environment/health-check row. <c>Ok</c> null = informational (no pass/fail).</summary>
    public record HealthItem(string Name, string Detail, bool? Ok);

    /// <summary>
    /// Structured environment health for the System Check page: informational rows (.NET, OS, viewer)
    /// plus pass/fail rows for the external tools FindNeedle depends on (tracefmt for ETL/WPP decode,
    /// WDK root, PlantUML for diagrams).
    /// </summary>
    public static System.Collections.Generic.List<HealthItem> GetHealthChecks()
    {
        var list = new System.Collections.Generic.List<HealthItem>
        {
            new(".NET runtime", RuntimeInformation.FrameworkDescription, null),
            new("Windows", GetWindowsVersion(), null),
            new("Default result viewer", FindNeedleUX.Services.ResultsViewerSettings.DefaultResultViewer.ToString(), null),
        };

        string? wdkRoot = null, tracefmt = null;
        try
        {
            var wdkType = Type.GetType("findneedle.WDK.WDKFinder, ETWPlugin", throwOnError: false);
            if (wdkType != null)
            {
                try { wdkRoot = wdkType.GetMethod("GetPathOfWDKRoot", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null)?.ToString(); }
                catch { /* not found */ }
                try { tracefmt = wdkType.GetMethod("GetTraceFmtPath", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null)?.ToString(); }
                catch { /* not found */ }
            }
        }
        catch { /* ETWPlugin not loaded */ }

        bool wdkOk = !string.IsNullOrWhiteSpace(wdkRoot) && Directory.Exists(wdkRoot);
        list.Add(new("WDK root", wdkOk ? wdkRoot! : "not found", wdkOk));

        bool tracefmtOk = !string.IsNullOrWhiteSpace(tracefmt) && File.Exists(tracefmt);
        list.Add(new("tracefmt (ETL / WPP decode)", tracefmtOk ? tracefmt! : "not found — WPP/.etl traces can't be decoded", tracefmtOk));

        // UML diagram generation — one row covering both renderers. OK if EITHER Mermaid or PlantUML is
        // available (a managed install via Diagram Tools, or for PlantUML a custom JAR path). The old
        // check only looked at PlantUML's custom config path, so a working managed install read "not set".
        var plantCustom = GetPlantUMLPath();
        bool plantCustomOk = !string.IsNullOrWhiteSpace(plantCustom) && File.Exists(plantCustom);
        FindNeedleToolInstallers.DependencyStatus plantStatus;
        try { plantStatus = GetPlantUmlStatus(); } catch { plantStatus = null; }
        FindNeedleToolInstallers.DependencyStatus mermaidStatus;
        try { mermaidStatus = GetMermaidStatus(); } catch { mermaidStatus = null; }
        bool plantOk = (plantStatus?.IsInstalled ?? false) || plantCustomOk;
        bool mermaidOk = mermaidStatus?.IsInstalled ?? false;
        var umlAvailable = new System.Collections.Generic.List<string>();
        if (mermaidOk) umlAvailable.Add("Mermaid");
        if (plantOk) umlAvailable.Add("PlantUML");
        bool umlOk = umlAvailable.Count > 0;
        list.Add(new("UML diagram generation",
            umlOk ? "available: " + string.Join(", ", umlAvailable)
                  : "no renderer installed — install Mermaid or PlantUML under Diagram Tools",
            umlOk));

        return list;
    }

    /// <summary>App version / build metadata for the About page.</summary>
    public static (string version, string source, string buildTime, string storeVersion) GetAboutInfo()
    {
        var (v, src) = GetAppVersionAndSource();
        return (v, src, GetBuildTime(), GetMsStoreVersion());
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

    /// <summary>
    /// Gets a formatted text describing UML diagram generation capabilities.
    /// </summary>
    public static string GetUmlCapabilitiesText()
    {
        var lines = new System.Collections.Generic.List<string>
        {
            "UML Diagram Capabilities:"
        };

        foreach (var status in UmlDependencyManager.GetAllStatuses())
        {
            var installed = status.IsInstalled ? "✓ Installed" : "✗ Not Installed";
            lines.Add($"  {status.Name}: {installed}");
            
            if (status.IsInstalled && !string.IsNullOrEmpty(status.InstalledPath))
            {
                lines.Add($"    Path: {status.InstalledPath}");
            }
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Gets the status of PlantUML installation.
    /// </summary>
    public static FindNeedleToolInstallers.DependencyStatus GetPlantUmlStatus() => UmlDependencyManager.PlantUml.GetStatus();

    /// <summary>
    /// Gets the status of Mermaid CLI installation.
    /// </summary>
    public static FindNeedleToolInstallers.DependencyStatus GetMermaidStatus() => UmlDependencyManager.Mermaid.GetStatus();

    /// <summary>
    /// Gets the Mermaid CLI version asynchronously.
    /// This is separate from GetMermaidStatus() because it can take several seconds.
    /// </summary>
    public static Task<string?> GetMermaidVersionAsync()
    {
        try
        {
            var status = UmlDependencyManager.Mermaid.GetStatus();
            return Task.FromResult(status?.InstalledVersion);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// Installs PlantUML asynchronously.
    /// </summary>
    public static Task<FindNeedleToolInstallers.InstallResult> InstallPlantUmlAsync(
        IProgress<FindNeedleToolInstallers.InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => UmlDependencyManager.PlantUml.InstallAsync(progress, cancellationToken);

    /// <summary>
    /// Installs Mermaid CLI asynchronously.
    /// </summary>
    public static Task<FindNeedleToolInstallers.InstallResult> InstallMermaidAsync(
        IProgress<FindNeedleToolInstallers.InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => UmlDependencyManager.Mermaid.InstallAsync(progress, cancellationToken);

    /// <summary>
    /// Gets the install directory for UML dependencies (for diagnostics).
    /// </summary>
    public static string GetUmlInstallDirectory() => 
        PackagedAppPaths.DependenciesBaseDir;
}
