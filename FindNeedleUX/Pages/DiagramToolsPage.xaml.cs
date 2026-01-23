using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FindNeedlePluginUtils.DependencyInstaller;

namespace FindNeedleUX.Pages;

/// <summary>
/// Page for managing UML diagram generation tools (PlantUML, Mermaid CLI).
/// </summary>
public sealed partial class DiagramToolsPage : Page
{
    private CancellationTokenSource? _installCancellationTokenSource;

    /// <summary>
    /// When true, PlantUML will use the web service instead of local Java.
    /// </summary>
    public static bool UseWebService { get; set; } = false;

    /// <summary>
    /// When true, test will fall back to browser mode if local generation fails.
    /// </summary>
    public static bool EnableBrowserFallback { get; set; } = false;

    public DiagramToolsPage()
    {
        this.InitializeComponent();
        InstallDirectoryText.Text = SystemInfoMiddleware.GetUmlInstallDirectory();
        UseWebServiceCheckBox.IsChecked = UseWebService;
        EnableBrowserFallbackCheckBox.IsChecked = EnableBrowserFallback;
        RefreshStatus();
    }

    private void UseWebServiceCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UseWebService = UseWebServiceCheckBox.IsChecked == true;
        FindNeedlePluginUtils.PlantUMLGenerator.UseWebServiceForGeneration = UseWebService;
        FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] UseWebService changed to: {UseWebService}");
    }

    private void EnableBrowserFallbackCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        EnableBrowserFallback = EnableBrowserFallbackCheckBox.IsChecked == true;
        FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] EnableBrowserFallback changed to: {EnableBrowserFallback}");
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
        MermaidStatusIcon.Text = mermaidStatus.IsInstalled ? "?" : "?";
        MermaidStatusIcon.Foreground = mermaidStatus.IsInstalled
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Green)
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
        MermaidStatusText.Text = mermaidStatus.IsInstalled ? "Installed" : "Not installed";
        MermaidPathDisplay.Text = mermaidStatus.InstalledPath ?? "Not found";
        MermaidVersionDisplay.Text = !string.IsNullOrEmpty(mermaidStatus.InstalledVersion) 
            ? $"Version: {mermaidStatus.InstalledVersion}" : "";
        InstallMermaidButton.Content = mermaidStatus.IsInstalled ? "Reinstall" : "Install";
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
                InstallProgressText.Text = $"Failed to install {name}: {result.ErrorMessage}";
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
            // Clear caches so generators pick up newly installed dependencies
            FindNeedlePluginUtils.PlantUMLGenerator.ClearCache();
            FindNeedlePluginUtils.MermaidUMLGenerator.ClearCache();

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
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
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

            string? outputPath = null;
            string outputType = "image";

            // Run the generator - try ImageFile first, fall back to Browser
            await Task.Run(() =>
            {
                if (toolName == "PlantUML")
                {
                    var generator = new FindNeedlePluginUtils.PlantUMLGenerator();
                    
                    // Try image file first
                    if (generator.IsSupported(FindNeedlePluginUtils.UmlOutputType.ImageFile))
                    {
                        try
                        {
                            outputPath = generator.GenerateUML(inputPath, FindNeedlePluginUtils.UmlOutputType.ImageFile);
                            outputType = "image";
                        }
                        catch (Exception imgEx)
                        {
                            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Image generation failed: {imgEx.Message}");
                            
                            // Only fall back to browser mode if enabled
                            if (!EnableBrowserFallback)
                            {
                                throw; // Re-throw the exception
                            }
                            
                            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Falling back to browser mode");
                            outputPath = generator.GenerateUML(inputPath, FindNeedlePluginUtils.UmlOutputType.Browser);
                            outputType = "browser";
                        }
                    }
                    else
                    {
                        // Use browser mode directly (if fallback enabled)
                        if (!EnableBrowserFallback)
                        {
                            throw new InvalidOperationException("Local image generation not supported and browser fallback is disabled.");
                        }
                        outputPath = generator.GenerateUML(inputPath, FindNeedlePluginUtils.UmlOutputType.Browser);
                        outputType = "browser";
                    }
                }
                else
                {
                    var generator = new FindNeedlePluginUtils.MermaidUMLGenerator();
                    
                    if (generator.IsSupported(FindNeedlePluginUtils.UmlOutputType.ImageFile))
                    {
                        try
                        {
                            outputPath = generator.GenerateUML(inputPath, FindNeedlePluginUtils.UmlOutputType.ImageFile);
                            outputType = "image";
                        }
                        catch (Exception imgEx)
                        {
                            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Image generation failed: {imgEx.Message}");
                            
                            if (!EnableBrowserFallback)
                            {
                                throw;
                            }
                            
                            FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Falling back to browser mode");
                            outputPath = generator.GenerateUML(inputPath, FindNeedlePluginUtils.UmlOutputType.Browser);
                            outputType = "browser";
                        }
                    }
                    else
                    {
                        if (!EnableBrowserFallback)
                        {
                            throw new InvalidOperationException("Local image generation not supported and browser fallback is disabled.");
                        }
                        outputPath = generator.GenerateUML(inputPath, FindNeedlePluginUtils.UmlOutputType.Browser);
                        outputType = "browser";
                    }
                }
            });

            // Check if output was created
            if (outputPath != null && File.Exists(outputPath))
            {
                var modeText = outputType == "browser" ? " (browser mode - uses web service)" : "";
                InstallProgressText.Text = $"{toolName} test successful{modeText}! Opening...";
                FindNeedlePluginLib.Logger.Instance.Log($"[DiagramToolsPage] Test successful! Output: {outputPath} (mode: {outputType})");

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
