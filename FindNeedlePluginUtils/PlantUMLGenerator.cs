using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using FindNeedlePluginLib;

namespace FindNeedlePluginUtils;

public class PlantUMLGenerator : IUMLGenerator
{
    private static string? _cachedPlantUMLPath = null;

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
            UmlOutputType.ImageFile => IsJavaRuntimeInstalled() && Path.Exists(GetPlantUMLPath()),
            _ => false
        };
    }

    private string GenerateImageFile(string inputPath)
    {
        ProcessStartInfo processStartInfo = new ProcessStartInfo
        {
            FileName = "java",
            Arguments = $"-jar \"{GetPlantUMLPath()}\" \"{inputPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process process = new Process
        {
            StartInfo = processStartInfo
        };
        process.Start();
        process.WaitForExit();

        var expectedOutput = Path.ChangeExtension(inputPath, ".png");
        if (Path.Exists(expectedOutput))
        {
            return expectedOutput;
        }

        throw new Exception("Failed to generate PlantUML image output");
    }

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

    public bool IsJavaRuntimeInstalled()
    {
        try
        {
            var rk = Registry.LocalMachine;
            var subKey = rk.OpenSubKey("SOFTWARE\\WOW6432Node\\JavaSoft\\Java Runtime Environment");

            if (subKey == null)
            {
                return false;
            }

            var currentVersion = subKey.GetValue("CurrentVersion")?.ToString();
            if (string.IsNullOrEmpty(currentVersion))
            {
                return false;
            }
            else
            {
                return true;
            }
        }
        catch
        {
            return false;
        }
    }
}
