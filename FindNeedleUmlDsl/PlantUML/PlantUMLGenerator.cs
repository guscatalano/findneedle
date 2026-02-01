using System;
using System.Diagnostics;
using System.IO;
using FindNeedlePluginLib;
using FindNeedlePluginUtils;
using FindNeedleToolInstallers;

namespace FindNeedleUmlDsl.PlantUML;

public class PlantUMLGenerator : IUMLGenerator
{
    private static string? _cachedPlantUMLPath = null;
    private static string? _cachedJavaPath = null;
    
    private readonly IPlantUmlInstaller? _installer;

    public PlantUMLGenerator(IPlantUmlInstaller? installer = null)
    {
        _installer = installer;
    }

    public void ClearCache()
    {
        _cachedPlantUMLPath = null;
        _cachedJavaPath = null;
    }

    public string Name => "PlantUML";
    public string InputFileExtension => ".pu";

    public UmlOutputType[] SupportedOutputTypes => IsSupported(UmlOutputType.ImageFile)
        ? [UmlOutputType.ImageFile, UmlOutputType.Browser]
        : IsSupported(UmlOutputType.Browser) ? [UmlOutputType.Browser] : [];

    public string GenerateUML(string inputPath, UmlOutputType outputType = UmlOutputType.ImageFile)
    {
        if (!File.Exists(inputPath))
            throw new Exception("Invalid input file path");

        if (!IsSupported(outputType))
            throw new Exception($"Output type '{outputType}' is not supported. Java and PlantUML jar are required.");

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
            UmlOutputType.ImageFile => GetJavaPath() != null && File.Exists(GetPlantUMLPath()),
            UmlOutputType.Browser => true,
            _ => false
        };
    }

    private string GenerateImageFile(string inputPath)
    {
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
        
        if (File.Exists(expectedOutput))
            File.Delete(expectedOutput);

        int exitCode = PackagedAppCommandRunner.RunJavaJar(javaPath, jarPath, inputPath, javaBinDir);

        Logger.Instance.Log($"[PlantUMLGenerator] Process completed, exit code: {exitCode}");
        Logger.Instance.Log($"[PlantUMLGenerator] Looking for output at: {expectedOutput}");

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

        var inputDir = Path.GetDirectoryName(inputPath);
        if (inputDir != null && Directory.Exists(inputDir))
        {
            var pngFiles = Directory.GetFiles(inputDir, "*.png");
            Logger.Instance.Log($"[PlantUMLGenerator] PNG files in input directory: {string.Join(", ", pngFiles)}");
        }

        var command = $"\"{javaPath}\" -jar \"{jarPath}\" \"{inputPath}\"";
        throw new Exception($"Failed to generate PlantUML image. Exit code: {exitCode}.\nCommand: {command}\nWorking directory: {javaBinDir}\nExpected output: {expectedOutput}");
    }

    private string GenerateBrowserHtml(string inputPath)
    {
        var plantUmlSource = File.ReadAllText(inputPath);
        var encodedSource = System.Web.HttpUtility.HtmlEncode(plantUmlSource);
        var outputPath = Path.ChangeExtension(inputPath, ".html");

        // Try to generate a local image if Java and PlantUML jar are available
        string imageHtml = "";
        try
        {
            if (GetJavaPath() != null && File.Exists(GetPlantUMLPath()))
            {
                var pngPath = GenerateImageFile(inputPath);
                var pngBytes = File.ReadAllBytes(pngPath);
                var base64Png = Convert.ToBase64String(pngBytes);
                imageHtml = $"<img src=\"data:image/png;base64,{base64Png}\" alt=\"PlantUML Diagram\" />";
                Logger.Instance.Log($"[PlantUMLGenerator] Using local generated image");
            }
        }
        catch { }

        // If we couldn't generate a local image, use the web service
        if (string.IsNullOrEmpty(imageHtml))
        {
            // Encode the PlantUML source for the web service
            var sourceBytes = System.Text.Encoding.UTF8.GetBytes(plantUmlSource);
            var base64Source = Convert.ToBase64String(sourceBytes);
            imageHtml = $"<img src=\"https://www.plantuml.com/plantuml/png/{base64Source}\" alt=\"PlantUML Diagram\" />";
            Logger.Instance.Log($"[PlantUMLGenerator] Using PlantUML web service");
        }

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
                    {{imageHtml}}
                </div>
                <h2>Source</h2>
                <pre>{{encodedSource}}</pre>
            </body>
            </html>
            """;

        File.WriteAllText(outputPath, html);
        Logger.Instance.Log($"[PlantUMLGenerator] Generated browser HTML: {outputPath}");
        return outputPath;
    }

    public string GetPlantUMLPath()
    {
        if (_cachedPlantUMLPath != null)
            return _cachedPlantUMLPath;

        if (_installer == null)
        {
            try
            {
                var mgr = new UmlDependencyManager();
                return mgr.PlantUml.GetPlantUmlJarPath() ?? "";
            }
            catch { }
        }

        var installerPath = _installer?.GetPlantUmlJarPath();
        if (installerPath != null)
        {
            _cachedPlantUMLPath = installerPath;
            return _cachedPlantUMLPath;
        }

        _cachedPlantUMLPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "plantuml-mit-1.2025.2.jar");
        return _cachedPlantUMLPath;
    }

    public string? GetJavaPath()
    {
        if (_cachedJavaPath != null)
        {
            Logger.Instance.Log($"[PlantUMLGenerator] Using cached Java path: {_cachedJavaPath}");
            return _cachedJavaPath;
        }

        var installerJava = _installer?.GetJavaPath();
        if (installerJava != null)
        {
            Logger.Instance.Log($"[PlantUMLGenerator] Found Java via installer: {installerJava}");
            _cachedJavaPath = installerJava;
            return _cachedJavaPath;
        }

        try
        {
            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                var javaFromHome = Path.Combine(javaHome, "bin", "java.exe");
                if (File.Exists(javaFromHome))
                {
                    Logger.Instance.Log($"[PlantUMLGenerator] Found Java via JAVA_HOME: {javaFromHome}");
                    _cachedJavaPath = javaFromHome;
                    return _cachedJavaPath;
                }
            }
        }
        catch { }

        try
        {
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(dir)) continue;
                    var candidate = Path.Combine(dir.Trim(), "java.exe");
                    if (File.Exists(candidate))
                    {
                        Logger.Instance.Log($"[PlantUMLGenerator] Found Java on PATH: {candidate}");
                        _cachedJavaPath = candidate;
                        return _cachedJavaPath;
                    }
                }
                catch { }
            }
        }
        catch { }

        Logger.Instance.Log($"[PlantUMLGenerator] No Java runtime found (installer, JAVA_HOME or PATH). Please install via the Diagram Tools page or ensure Java is on PATH.");
        return null;
    }
}
