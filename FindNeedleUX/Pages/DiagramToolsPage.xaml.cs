using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FindNeedleToolInstallers;
using FindNeedlePluginUtils;
using FindNeedlePluginLib;
using FindNeedleUmlDsl;

namespace FindNeedleUX.Pages;

/// <summary>
/// Page for managing UML diagram generation tools (PlantUML, Mermaid CLI).
/// </summary>
public sealed partial class DiagramToolsPage : Page
{
    private CancellationTokenSource? _installCancellationTokenSource;
    private UmlOutputType _preferredOutputType = UmlOutputType.ImageFile;

    public DiagramToolsPage()
    {
        this.InitializeComponent();
        InstallDirectoryText.Text = SystemInfoMiddleware.GetUmlInstallDirectory();
        RefreshStatus();
    }

    private void OpenDemo_Click(object sender, RoutedEventArgs e)
    {
        // kept for compatibility but UI now exposes explicit external/in-app options
    }

    private void OpenDemoExternal_Click(object sender, RoutedEventArgs e)
    {
        FindNeedlePluginLib.Logger.Instance.Log("[DiagramToolsPage] OpenDemoExternal_Click invoked");
        InstallProgressPanel.Visibility = Visibility.Visible;
        InstallProgressText.Text = "Opening demo (external)...";
        try
        {
            var fullPath = FindTimelineDemoFullPath();
            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] FindTimelineDemoFullPath -> {fullPath}");
            if (fullPath == null)
            {
                InstallProgressText.Text = "Could not find timeline-demo.html in expected locations.";
                var notFoundDlg = new ContentDialog() { Title = "Demo file not found", Content = "Could not locate timeline-demo.html. Check that WebContent/timeline-demo.html is present.", CloseButtonText = "OK" };
                DispatcherQueue.TryEnqueue(() => { try { notFoundDlg.XamlRoot = this.XamlRoot; } catch { } _ = notFoundDlg.ShowAsync(); });
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = fullPath, UseShellExecute = true });
            InstallProgressPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] OpenDemoExternal failed: {ex.Message}");
            InstallProgressPanel.Visibility = Visibility.Visible;
            InstallProgressText.Text = $"Could not open demo (external): {ex.Message}";
        }
    }

    private async void OpenDemoInApp_Click(object sender, RoutedEventArgs e)
    {
        FindNeedlePluginLib.Logger.Instance.Log("[DiagramToolsPage] OpenDemoInApp_Click invoked");
        InstallProgressPanel.Visibility = Visibility.Visible;
        InstallProgressText.Text = "Opening demo in new window...";
        try
        {
            var fullPath = FindTimelineDemoFullPath();
            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] FindTimelineDemoFullPath -> {fullPath}");
            if (fullPath == null)
            {
                InstallProgressText.Text = "Could not find timeline-demo.html in expected locations.";
                var notFoundDlg = new ContentDialog() { Title = "Demo file not found", Content = "Could not locate timeline-demo.html. Check that WebContent/timeline-demo.html is present.", CloseButtonText = "OK" };
                DispatcherQueue.TryEnqueue(() => { try { notFoundDlg.XamlRoot = this.XamlRoot; } catch { } _ = notFoundDlg.ShowAsync(); });
                return;
            }
            var demoWindow = new FindNeedleUX.Windows.DemoViewerWindow(fullPath);
            demoWindow.Activate();
            InstallProgressPanel.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] OpenDemoInApp failed: {ex.Message}");
            InstallProgressPanel.Visibility = Visibility.Visible;
            InstallProgressText.Text = $"Could not open demo window: {ex.Message}";
        }
    }

    // Try multiple candidate locations to find timeline-demo.html. Returns full path or null.
    private string? FindTimelineDemoFullPath()
    {
        // Try obvious locations relative to AppContext.BaseDirectory
        var candidates = new System.Collections.Generic.List<string>();
        var baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "WebContent", "timeline-demo.html"));
        candidates.Add(Path.Combine(baseDir, "..", "WebContent", "timeline-demo.html"));
        candidates.Add(Path.Combine(baseDir, "..", "..", "FindNeedleUX", "WebContent", "timeline-demo.html"));

        // Walk parent dirs to find repository layout
        var dirInfo = new DirectoryInfo(baseDir);
        for (int i = 0; i < 6 && dirInfo != null; i++)
        {
            var tryPath = Path.Combine(dirInfo.FullName, "FindNeedleUX", "WebContent", "timeline-demo.html");
            candidates.Add(tryPath);
            var tryPath2 = Path.Combine(dirInfo.FullName, "WebContent", "timeline-demo.html");
            candidates.Add(tryPath2);
            dirInfo = dirInfo.Parent;
        }

        foreach (var c in candidates)
        {
            try
            {
                var full = Path.GetFullPath(c);
                if (File.Exists(full)) return full;
            }
            catch { }
        }

        // If packaged, try package installed location (UWP/WinUI)
        try
        {
            var pkg = global::Windows.ApplicationModel.Package.Current;
            if (pkg != null)
            {
                var installed = pkg.InstalledLocation.Path;
                var p = Path.Combine(installed, "WebContent", "timeline-demo.html");
                if (File.Exists(p)) return p;
            }
        }
        catch { }

        return null;
    }

    private void OutputFormat_Changed(object sender, RoutedEventArgs e)
    {
        _preferredOutputType = OutputPngRadio.IsChecked == true 
            ? UmlOutputType.ImageFile 
            : UmlOutputType.Browser;
        FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Output format changed to: {_preferredOutputType}");
    }

    private void RefreshStatus()
    {
        // Update PlantUML status
        var plantUmlStatus = SystemInfoMiddleware.GetPlantUmlStatus();
        PlantUmlStatusIcon.Text = plantUmlStatus.IsInstalled ? "✓" : "✗";
        PlantUmlStatusIcon.Foreground = plantUmlStatus.IsInstalled
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green)
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
        PlantUmlStatusText.Text = plantUmlStatus.IsInstalled ? "Installed" : "Not installed";
        PlantUmlPathDisplay.Text = plantUmlStatus.InstalledPath ?? "Not found";
        PlantUmlVersionDisplay.Text = !string.IsNullOrEmpty(plantUmlStatus.InstalledVersion) 
            ? $"Version: {plantUmlStatus.InstalledVersion}" : "";
        InstallPlantUmlButton.Content = plantUmlStatus.IsInstalled ? "Reinstall" : "Install";

        // Update Mermaid status
        var mermaidStatus = SystemInfoMiddleware.GetMermaidStatus();
        MermaidStatusIcon.Text = mermaidStatus.IsInstalled ? "✓" : "✗";
        MermaidStatusIcon.Foreground = mermaidStatus.IsInstalled
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green)
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
        MermaidStatusText.Text = mermaidStatus.IsInstalled ? "Installed" : "Not installed";
        MermaidPathDisplay.Text = mermaidStatus.InstalledPath ?? "Not found";
        InstallMermaidButton.Content = mermaidStatus.IsInstalled ? "Reinstall" : "Install";
        
        // For Mermaid, fetch version asynchronously since it's slow
        if (mermaidStatus.IsInstalled)
        {
            MermaidVersionDisplay.Text = "Version: checking...";
            _ = FetchMermaidVersionAsync();
        }
        else
        {
            MermaidVersionDisplay.Text = "";
        }
    }

    private async Task FetchMermaidVersionAsync()
    {
        try
        {
            var version = await SystemInfoMiddleware.GetMermaidVersionAsync();
            DispatcherQueue.TryEnqueue(() =>
            {
                MermaidVersionDisplay.Text = !string.IsNullOrEmpty(version) 
                    ? $"Version: {version}" 
                    : "";
            });
        }
        catch (Exception ex)
        {
            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Failed to get Mermaid version: {ex.Message}");
            DispatcherQueue.TryEnqueue(() =>
            {
                MermaidVersionDisplay.Text = "";
            });
        }
    }

    private async void InstallPlantUml_Click(object sender, RoutedEventArgs e)
    {
        await InstallDependencyAsync("PlantUML",
            (progress, ct) => SystemInfoMiddleware.InstallPlantUmlAsync(progress, ct));
    }

    private async void InstallMermaid_Click(object sender, RoutedEventArgs e)
    {
        await InstallDependencyAsync("Mermaid CLI",
            (progress, ct) => SystemInfoMiddleware.InstallMermaidAsync(progress, ct));
    }

    private async Task InstallDependencyAsync(string name, Func<IProgress<InstallProgress>?, CancellationToken, Task<InstallResult>> installFunc)
    {
        // Disable buttons during install
        InstallPlantUmlButton.IsEnabled = false;
        InstallMermaidButton.IsEnabled = false;
        InstallProgressPanel.Visibility = Visibility.Visible;
        InstallProgressBar.IsIndeterminate = true;

        _installCancellationTokenSource = new CancellationTokenSource();

        var progress = new Progress<InstallProgress>(p =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                InstallProgressText.Text = p.Status;
                if (!p.IsIndeterminate)
                {
                    InstallProgressBar.IsIndeterminate = false;
                    InstallProgressBar.Value = p.PercentComplete;
                }
            });
        });

        try
        {
            InstallProgressText.Text = $"Installing {name}...";
            var result = await installFunc(progress, _installCancellationTokenSource.Token);

            if (result.Success)
            {
                InstallProgressText.Text = $"{name} installed successfully!";
            }
            else
            {
                InstallProgressText.Text = $"Failed to install {name}: {result.Message}";
            }
        }
        catch (OperationCanceledException)
        {
            InstallProgressText.Text = "Installation cancelled.";
        }
        catch (Exception ex)
        {
            InstallProgressText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            // Re-enable buttons and refresh status
            InstallPlantUmlButton.IsEnabled = true;
            InstallMermaidButton.IsEnabled = true;
            InstallProgressBar.IsIndeterminate = false;

            // Delay hiding progress panel so user can see result
            await Task.Delay(3000);
            InstallProgressPanel.Visibility = Visibility.Collapsed;

            RefreshStatus();
        }
    }


    private void OpenInstallFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = SystemInfoMiddleware.GetUmlInstallDirectory();
        try
        {
            // Create directory if it doesn't exist
            Directory.CreateDirectory(path);
            
            // For packaged apps, the path is virtualized. We need to get the actual location.
            string actualPath = path;
            if (FindNeedlePluginUtils.PackagedAppPaths.IsPackagedApp)
            {
                // For packaged apps, LocalAppData is virtualized to the package's LocalCache\Local folder
                // The actual path is: %LOCALAPPDATA%\Packages\{PackageFamilyName}\LocalCache\Local\FindNeedle\Dependencies
                var packageFamilyName = FindNeedlePluginUtils.PackagedAppPaths.PackageFamilyName;
                if (!string.IsNullOrEmpty(packageFamilyName))
                {
                    var realLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    actualPath = Path.Combine(realLocalAppData, "Packages", packageFamilyName, "LocalCache", "Local", "FindNeedle", "Dependencies");
                    
                    // Ensure directory exists
                    Directory.CreateDirectory(actualPath);
                    
                    
                    FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Opening virtualized path: {actualPath}");
                }
            }
            
            Process.Start(new ProcessStartInfo
            {
                FileName = actualPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            InstallProgressPanel.Visibility = Visibility.Visible;
            InstallProgressText.Text = $"Could not open folder: {ex.Message}";
            InstallProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenTestOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        // Test output goes to the system temp folder, which is NOT virtualized for packaged apps
        var path = Path.Combine(Path.GetTempPath(), "FindNeedle_DiagramTest");
        try
        {
            // Create directory if it doesn't exist
            Directory.CreateDirectory(path);
            
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            
            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Opened test output folder: {path}");
        }
        catch (Exception ex)
        {
            InstallProgressPanel.Visibility = Visibility.Visible;
            InstallProgressText.Text = $"Could not open folder: {ex.Message}";
            InstallProgressBar.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenCommandPrompt_Click(object sender, RoutedEventArgs e)
    {
        var path = SystemInfoMiddleware.GetUmlInstallDirectory();
        try
        {
            // Create directory if it doesn't exist
            Directory.CreateDirectory(path);
            
            // Get the package family name
            string? packageFamilyName = null;
            string? appId = null;
            try
            {
                packageFamilyName = global::Windows.ApplicationModel.Package.Current.Id.FamilyName;
                appId = global::Windows.ApplicationModel.Package.Current.Id.Name;
            }
            catch
            {
                // Not a packaged app
            }

            if (!string.IsNullOrEmpty(packageFamilyName))
            {
                // For packaged apps, use PowerShell with Invoke-CommandInDesktopPackage
                // This opens cmd.exe in the package context so it can access virtualized paths
                var psScript = $@"
Write-Host 'Package Family: {packageFamilyName}' -ForegroundColor Cyan
Write-Host 'Install Path: {path}' -ForegroundColor Cyan
Write-Host ''
Write-Host 'Starting cmd.exe in package context...' -ForegroundColor Yellow
Write-Host ''
Invoke-CommandInDesktopPackage -Command 'cmd.exe' -PackageFamilyName '{packageFamilyName}' -AppId 'App' -PreventBreakaway
";
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoExit -Command \"{psScript.Replace("\"", "`\"")}\"",
                    UseShellExecute = true
                });
                
                FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Opening packaged command prompt via Invoke-CommandInDesktopPackage");
            }
            else
            {
                // For unpackaged apps, just open cmd.exe directly
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k cd /d \"{path}\"",
                    UseShellExecute = true
                });
                
                FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Opened command prompt at: {path}");
            }
        }
        catch (Exception ex)
        {
            InstallProgressPanel.Visibility = Visibility.Visible;
            InstallProgressText.Text = $"Could not open command prompt: {ex.Message}";
            InstallProgressBar.Visibility = Visibility.Collapsed;
            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Failed to open command prompt: {ex.Message}");
        }
    }

    private async void TestPlantUml_Click(object sender, RoutedEventArgs e)
    {
        await TestDiagramGeneratorAsync("PlantUML", ".puml", GeneratePlantUmlTestContent());
    }

    private async void TestMermaid_Click(object sender, RoutedEventArgs e)
    {
        await TestDiagramGeneratorAsync("Mermaid", ".mmd", GenerateMermaidTestContent());
    }

    private static string GeneratePlantUmlTestContent()
    {
        return """
            @startuml
            title Test Diagram - PlantUML
            
            actor User
            participant "FindNeedle" as App
            database "Log Files" as Logs
            
            User -> App : Search logs
            App -> Logs : Query
            Logs --> App : Results
            App --> User : Display results
            
            note right of App : PlantUML is working!
            @enduml
            """;
    }

    private static string GenerateMermaidTestContent()
    {
        return """
            sequenceDiagram
                title Test Diagram - Mermaid
                
                actor User
                participant App as FindNeedle
                participant Logs as Log Files
                
                User->>App: Search logs
                App->>Logs: Query
                Logs-->>App: Results
                App-->>User: Display results
                
                Note right of App: Mermaid is working!
            """;
    }


    private async Task TestDiagramGeneratorAsync(string toolName, string extension, string content)
    {
        TestPlantUmlButton.IsEnabled = false;
        TestMermaidButton.IsEnabled = false;
        InstallProgressPanel.Visibility = Visibility.Visible;
        InstallProgressText.Text = $"Testing {toolName}...";
        InstallProgressBar.IsIndeterminate = true;

        try
        {
            // Create temp file with test content
            var tempDir = Path.Combine(Path.GetTempPath(), "FindNeedle_DiagramTest");
            Directory.CreateDirectory(tempDir);
            var inputPath = Path.Combine(tempDir, $"test{extension}");
            var pngPath = Path.ChangeExtension(inputPath, ".png");
            var htmlPath = Path.ChangeExtension(inputPath, ".html");

            // Clean up any previous test files
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(pngPath)) File.Delete(pngPath);
            if (File.Exists(htmlPath)) File.Delete(htmlPath);

            // Write test content
            await File.WriteAllTextAsync(inputPath, content);
            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Test file created: {inputPath}");

            // Determine output type to use
            var outputType = _preferredOutputType;

            string? outputPath = null;

            // Run the generator
            await Task.Run(() =>
            {
                if (toolName == "PlantUML")
                {
                    var manager = SystemInfoMiddleware.UmlDependencyManager;
                    var generator = new PlantUMLGenerator(manager.PlantUml);
                    if (!generator.IsSupported(outputType))
                    {
                        // Fall back to the other type if preferred is not supported
                        var fallbackType = outputType == UmlOutputType.ImageFile ? UmlOutputType.Browser : UmlOutputType.ImageFile;
                        if (generator.IsSupported(fallbackType))
                        {
                            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] {outputType} not supported, falling back to {fallbackType}");
                            outputType = fallbackType;
                        }
                        else
                        {
                            throw new InvalidOperationException("PlantUML is not installed. Please install via the Install button.");
                        }
                    }
                    
                    outputPath = generator.GenerateUML(inputPath, outputType);
                }
                else
                {
                    var manager = SystemInfoMiddleware.UmlDependencyManager;
                    var generator = new MermaidUMLGenerator(manager.Mermaid);
                    if (!generator.IsSupported(outputType))
                    {
                        // Fall back to the other type if preferred is not supported
                        var fallbackType = outputType == UmlOutputType.ImageFile ? UmlOutputType.Browser : UmlOutputType.ImageFile;
                        if (generator.IsSupported(fallbackType))
                        {
                            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] {outputType} not supported, falling back to {fallbackType}");
                            outputType = fallbackType;
                        }
                        else
                        {
                            throw new InvalidOperationException("Mermaid CLI is not installed. Please install via the Install button.");
                        }
                    }
                    
                    outputPath = generator.GenerateUML(inputPath, outputType);
                }
            });

            // Check if output was created
            if (outputPath != null && File.Exists(outputPath))
            {
                var formatText = outputType == UmlOutputType.Browser ? " (HTML)" : " (PNG)";
                InstallProgressText.Text = $"{toolName} test successful{formatText}! Opening...";
                FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Test successful! Output: {outputPath}");

                // Open the generated file
                Process.Start(new ProcessStartInfo
                {
                    FileName = outputPath,
                    UseShellExecute = true
                });
                
                // Auto-hide on success
                await Task.Delay(3000);
                InstallProgressPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                InstallProgressText.Text = $"{toolName} test failed: Output file not created";
                FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Test failed: output not created");
                // Keep panel open on failure - don't auto-hide
            }
        }
        catch (Exception ex)
        {
            // Show the full error message including the command
            InstallProgressText.Text = $"{toolName} test failed:\n{ex.Message}";
            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Test failed with exception: {ex}");
            // Keep panel open on failure - don't auto-hide
        }
        finally
        {
            TestPlantUmlButton.IsEnabled = true;
            TestMermaidButton.IsEnabled = true;
            InstallProgressBar.IsIndeterminate = false;
        }
    }
}
