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

    public UmlOutputType[] SupportedOutputTypes => IsSupported(UmlOutputType.ImageFile)
        ? [UmlOutputType.ImageFile, UmlOutputType.Browser]
        : [UmlOutputType.Browser];

    public string GenerateUML(string inputPath, UmlOutputType outputType = UmlOutputType.ImageFile)
    {
        if (!Path.Exists(inputPath))
        {
            throw new Exception("Invalid input file path");
        }

        if (!IsSupported(outputType))
        {
            throw new Exception($"Output type '{outputType}' is not supported. Java and PlantUML jar are required for image generation.");
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
            UmlOutputType.Browser => true, // Always supported
            UmlOutputType.ImageFile => GetJavaPath() != null && File.Exists(GetPlantUMLPath()),
            _ => false
        };
    }

    private string GenerateImageFile(string inputPath)
    {
        var plantUmlContent = File.ReadAllText(inputPath);
        var outputPath = Path.ChangeExtension(inputPath, ".png");

        // Check if user wants to use web service
        if (UseWebServiceForGeneration)
        {
            Logger.Instance.Log($"[PlantUMLGenerator] Using web service (user preference)");
            return GenerateImageViaWebService(plantUmlContent, outputPath);
        }

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

        // Check if we're running as a packaged app
        string? packageFamilyName = null;
        try
        {
            packageFamilyName = global::Windows.ApplicationModel.Package.Current.Id.FamilyName;
        }
        catch
        {
            // Not a packaged app
        }

        int exitCode;
        if (!string.IsNullOrEmpty(packageFamilyName))
        {
            // For packaged apps, use Invoke-CommandInDesktopPackage to run Java in the correct context
            Logger.Instance.Log($"[PlantUMLGenerator] Running as packaged app, using Invoke-CommandInDesktopPackage");
            exitCode = RunJavaViaPackageContext(packageFamilyName, javaPath, jarPath, inputPath, javaBinDir);
        }
        else
        {
            // For unpackaged apps, run Java directly
            Logger.Instance.Log($"[PlantUMLGenerator] Running as unpackaged app, launching Java directly");
            exitCode = RunJavaDirectly(javaPath, jarPath, inputPath, javaBinDir);
        }

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

    private int RunJavaViaPackageContext(string packageFamilyName, string javaPath, string jarPath, string inputPath, string workingDir)
    {
        // Build the command to run Java
        var javaCommand = $"\"{javaPath}\" -jar \"{jarPath}\" \"{inputPath}\"";
        
        // Use PowerShell to invoke the command in the desktop package context
        var psCommand = $"Invoke-CommandInDesktopPackage -PackageFamilyName '{packageFamilyName}' -AppId 'App' -Command 'cmd.exe' -Args '/c cd /d \"{workingDir}\" && {javaCommand}' -PreventBreakaway";
        
        Logger.Instance.Log($"[PlantUMLGenerator] PowerShell command: {psCommand}");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDir
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new Exception("Failed to start PowerShell process");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60000);

        if (!string.IsNullOrWhiteSpace(stdout))
            Logger.Instance.Log($"[PlantUMLGenerator] PowerShell stdout: {stdout}");
        if (!string.IsNullOrWhiteSpace(stderr))
            Logger.Instance.Log($"[PlantUMLGenerator] PowerShell stderr: {stderr}");

        return process.ExitCode;
    }

    private int RunJavaDirectly(string javaPath, string jarPath, string inputPath, string workingDir)
    {
        ProcessStartInfo processStartInfo = new ProcessStartInfo
        {
            FileName = javaPath,
            Arguments = $"-jar \"{jarPath}\" \"{inputPath}\"",
            UseShellExecute = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDir
        };

        Logger.Instance.Log($"[PlantUMLGenerator] Starting Java via shell...");

        using var process = Process.Start(processStartInfo);
        if (process == null)
        {
            throw new Exception("Failed to start Java process");
        }

        // Wait for process to complete (with timeout)
        var completed = process.WaitForExit(60000); // 60 second timeout
        
        if (!completed)
        {
            try { process.Kill(); } catch { }
            throw new Exception("PlantUML generation timed out after 60 seconds");
        }

        return process.ExitCode;
    }

    private string GenerateImageViaWebService(string plantUmlContent, string outputPath)
    {
        var encoded = EncodePlantUmlForWeb(plantUmlContent);
        var url = $"https://www.plantuml.com/plantuml/png/{encoded}";

        Logger.Instance.Log($"[PlantUMLGenerator] Downloading PNG from PlantUML web service...");

        using var httpClient = new System.Net.Http.HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        try
        {
            var response = httpClient.GetAsync(url).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();

            var pngBytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            File.WriteAllBytes(outputPath, pngBytes);

            Logger.Instance.Log($"[PlantUMLGenerator] PNG downloaded successfully: {outputPath} ({pngBytes.Length} bytes)");
            return outputPath;
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[PlantUMLGenerator] Web service failed: {ex.Message}");
            throw new Exception($"Failed to generate PlantUML diagram via web service: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Gets or sets whether to use the web service for PNG generation instead of local Java.
    /// This is set by the DiagramToolsPage checkbox.
    /// </summary>
    public static bool UseWebServiceForGeneration { get; set; } = false;

    private string GenerateBrowserHtml(string inputPath)
    {
        var plantUmlContent = File.ReadAllText(inputPath);
        var outputPath = Path.ChangeExtension(inputPath, ".html");

        // PlantUML uses a specific encoding for web rendering
        var encoded = EncodePlantUmlForWeb(plantUmlContent);
        var encodedSource = System.Web.HttpUtility.HtmlEncode(plantUmlContent);

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
                    <img src="https://www.plantuml.com/plantuml/svg/{{encoded}}" alt="PlantUML Diagram" />
                </div>
                <h2>Source</h2>
                <pre>{{encodedSource}}</pre>
            </body>
            </html>
            """;

        File.WriteAllText(outputPath, html);
        return outputPath;
    }

    private static string EncodePlantUmlForWeb(string plantUml)
    {
        // PlantUML web service uses a custom encoding
        var compressed = Deflate(System.Text.Encoding.UTF8.GetBytes(plantUml));
        return Encode64(compressed);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
        {
            deflate.Write(data, 0, data.Length);
        }
        return output.ToArray();
    }

    private static string Encode64(byte[] data)
    {
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < data.Length; i += 3)
        {
            int b1 = data[i] & 0xFF;
            int b2 = (i + 1 < data.Length) ? data[i + 1] & 0xFF : 0;
            int b3 = (i + 2 < data.Length) ? data[i + 2] & 0xFF : 0;

            result.Append(Encode6bit(b1 >> 2));
            result.Append(Encode6bit(((b1 & 0x3) << 4) | (b2 >> 4)));
            result.Append(Encode6bit(((b2 & 0xF) << 2) | (b3 >> 6)));
            result.Append(Encode6bit(b3 & 0x3F));
        }
        return result.ToString();
    }

    private static char Encode6bit(int b)
    {
        if (b < 10) return (char)(48 + b);        // 0-9
        b -= 10;
        if (b < 26) return (char)(65 + b);        // A-Z
        b -= 26;
        if (b < 26) return (char)(97 + b);        // a-z
        b -= 26;
        if (b == 0) return '-';
        return '_';
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
