using System;
using System.Diagnostics;
using System.IO;
using FindNeedlePluginLib;
using FindNeedlePluginUtils.DependencyInstaller;

namespace FindNeedlePluginUtils;

public class PlantUMLGenerator : IUMLGenerator
{
    private static string? _cachedPlantUMLPath = null;
    private static string? _cachedJavaPath = null;
    private static PlantUmlInstaller? _installer = null;

    private static PlantUmlInstaller Installer => _installer ??= new PlantUmlInstaller();

    /// <summary>
    /// Clears cached paths. Call after installing dependencies.
    /// </summary>
    public static void ClearCache()
    {
        _cachedPlantUMLPath = null;
        _cachedJavaPath = null;
        _installer = null;
    }

    public string Name => "PlantUML";

    public string InputFileExtension => ".pu";

    // ImageFile generates PNG locally; Browser mode wraps the PNG in HTML for viewing
    public UmlOutputType[] SupportedOutputTypes => IsSupported(UmlOutputType.ImageFile)
        ? [UmlOutputType.ImageFile, UmlOutputType.Browser]
        : [];

    public string GenerateUML(string inputPath, UmlOutputType outputType = UmlOutputType.ImageFile)
    {
        if (!Path.Exists(inputPath))
        {
            throw new Exception("Invalid input file path");
        }

        if (!IsSupported(outputType))
        {
            throw new Exception($"Output type '{outputType}' is not supported. Java and PlantUML jar are required.");
        }

        return outputType switch
        {
            UmlOutputType.ImageFile => GenerateImageFile(inputPath),
            UmlOutputType.Browser => GenerateBrowserHtml(inputPath),
            _ => throw new ArgumentException($"Unknown output type: {outputType}")
        };
    }

    public bool IsSupported(UmlOutputType outputType)
    {
        return outputType switch
        {
            // Both modes require local PlantUML installation
            UmlOutputType.ImageFile => GetJavaPath() != null && File.Exists(GetPlantUMLPath()),
            UmlOutputType.Browser => GetJavaPath() != null && File.Exists(GetPlantUMLPath()),
            _ => false
        };
    }

    private string GenerateImageFile(string inputPath)
    {
        var plantUmlContent = File.ReadAllText(inputPath);
        var outputPath = Path.ChangeExtension(inputPath, ".png");

        var javaPath = GetJavaPath();
        var jarPath = GetPlantUMLPath();

        if (javaPath == null)
            throw new InvalidOperationException("Java runtime not found. Please install via the Diagram Tools page, or enable 'Use web service' option.");

        var javaBinDir = Path.GetDirectoryName(javaPath);
        if (javaBinDir == null)
            throw new InvalidOperationException("Could not determine Java bin directory.");

        Logger.Instance.Log($"[PlantUMLGenerator] Using Java at: {javaPath}");
        Logger.Instance.Log($"[PlantUMLGenerator] PlantUML JAR: {jarPath}");
        Logger.Instance.Log($"[PlantUMLGenerator] Input file: {inputPath}");

        var expectedOutput = Path.ChangeExtension(inputPath, ".png");
        
        // Delete existing output to detect if generation succeeds
        if (File.Exists(expectedOutput))
            File.Delete(expectedOutput);

        // Run Java via the packaged app command runner (handles MSIX context automatically)
        int exitCode = PackagedAppCommandRunner.RunJavaJar(javaPath, jarPath, inputPath, javaBinDir);

        Logger.Instance.Log($"[PlantUMLGenerator] Process completed, exit code: {exitCode}");
        Logger.Instance.Log($"[PlantUMLGenerator] Looking for output at: {expectedOutput}");

        // Wait a bit for the file to be written (especially when using Invoke-CommandInDesktopPackage)
        // Poll for the file up to 10 seconds
        for (int i = 0; i < 20; i++)
        {
            if (File.Exists(expectedOutput))
            {
                Logger.Instance.Log($"[PlantUMLGenerator] Successfully generated: {expectedOutput}");
                return expectedOutput;
            }
            System.Threading.Thread.Sleep(500);
            Logger.Instance.Log($"[PlantUMLGenerator] Waiting for output file... ({i + 1}/20)");
        }

        // Check if file was created in a different location (virtualized path)
        var inputDir = Path.GetDirectoryName(inputPath);
        if (inputDir != null && Directory.Exists(inputDir))
        {
            var pngFiles = Directory.GetFiles(inputDir, "*.png");
            Logger.Instance.Log($"[PlantUMLGenerator] PNG files in input directory: {string.Join(", ", pngFiles)}");
        }

        // Include the command in the error for debugging
        var command = $"\"{javaPath}\" -jar \"{jarPath}\" \"{inputPath}\"";
        throw new Exception($"Failed to generate PlantUML image. Exit code: {exitCode}.\nCommand: {command}\nWorking directory: {javaBinDir}\nExpected output: {expectedOutput}");
    }

    private string GenerateBrowserHtml(string inputPath)
    {
        // First generate the PNG locally
        var pngPath = GenerateImageFile(inputPath);
        
        // Read the PNG and convert to base64 data URI (so HTML is self-contained)
        var pngBytes = File.ReadAllBytes(pngPath);
        var base64Png = Convert.ToBase64String(pngBytes);
        
        var plantUmlSource = File.ReadAllText(inputPath);
        var encodedSource = System.Web.HttpUtility.HtmlEncode(plantUmlSource);
        
        var outputPath = Path.ChangeExtension(inputPath, ".html");

        var html = $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <title>PlantUML Diagram</title>
                <style>
                    body { 
                        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                        margin: 20px;
                        background: #f5f5f5;
                    }
                    .diagram { 
                        background: white; 
                        padding: 20px; 
                        border-radius: 8px;
                        box-shadow: 0 2px 4px rgba(0,0,0,0.1);
                    }
                    h1 { color: #333; font-size: 1.2em; }
                    pre { 
                        background: #f8f8f8; 
                        padding: 15px; 
                        border-radius: 4px;
                        overflow-x: auto;
                    }
                    img { max-width: 100%; height: auto; }
                </style>
            </head>
            <body>
                <h1>PlantUML Diagram</h1>
                <div class="diagram">
                    <img src="data:image/png;base64,{{base64Png}}" alt="PlantUML Diagram" />
                </div>
                <h2>Source</h2>
                <pre>{{encodedSource}}</pre>
            </body>
            </html>
            """;

        File.WriteAllText(outputPath, html);
        Logger.Instance.Log($"[PlantUMLGenerator] Generated browser HTML with embedded PNG: {outputPath}");
        return outputPath;
    }

    public string GetPlantUMLPath()
    {
        if (_cachedPlantUMLPath != null)
            return _cachedPlantUMLPath;

        // First check if installed via our installer
        var installerPath = Installer.GetPlantUmlJarPath();
        if (installerPath != null)
        {
            _cachedPlantUMLPath = installerPath;
            return _cachedPlantUMLPath;
        }

        // Use PluginSubsystemAccessorProvider if available
        var accessor = PluginSubsystemAccessorProvider.Accessor;
        if (accessor != null && !string.IsNullOrWhiteSpace(accessor.PlantUMLPath))
        {
            _cachedPlantUMLPath = accessor.PlantUMLPath;
            return _cachedPlantUMLPath;
        }

        // Fallback to default
        _cachedPlantUMLPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "plantuml-mit-1.2025.2.jar");
        return _cachedPlantUMLPath;
    }

    /// <summary>
    /// Gets the path to the Java executable, checking local installation first.
    /// </summary>
    public string? GetJavaPath()
    {
        if (_cachedJavaPath != null)
        {
            Logger.Instance.Log($"[PlantUMLGenerator] Using cached Java path: {_cachedJavaPath}");
            return _cachedJavaPath;
        }

        // ONLY use our portable Java installation to avoid system Java issues (jli.dll errors)
        var installerJava = Installer.GetJavaPath();
        if (installerJava != null)
        {
            Logger.Instance.Log($"[PlantUMLGenerator] Found Java via installer: {installerJava}");
            _cachedJavaPath = installerJava;
            return _cachedJavaPath;
        }

        Logger.Instance.Log($"[PlantUMLGenerator] No portable Java installation found. Please install via Diagram Tools page.");
        return null;
    }

}
