using System;
using System.Diagnostics;
using System.IO;

namespace FindNeedlePluginUtils;

public class MermaidUMLGenerator : IUMLGenerator
{
    private static string? _cachedMermaidCliPath = null;
    private bool? _mermaidCliAvailable = null;

    public string Name => "Mermaid";

    public string InputFileExtension => ".mmd";

    public UmlOutputType[] SupportedOutputTypes => IsSupported(UmlOutputType.ImageFile)
        ? [UmlOutputType.ImageFile, UmlOutputType.Browser]
        : [UmlOutputType.Browser];

    public string GenerateUML(string inputPath, UmlOutputType outputType = UmlOutputType.ImageFile)
    {
        if (!File.Exists(inputPath))
        {
            throw new Exception("Invalid input file path");
        }

        if (!IsSupported(outputType))
        {
            throw new Exception($"Output type '{outputType}' is not supported. Mermaid CLI (mmdc) is required for image generation.");
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
            UmlOutputType.ImageFile => IsMermaidCliAvailable(),
            _ => false
        };
    }

    private string GenerateImageFile(string inputPath)
    {
        var outputPath = Path.ChangeExtension(inputPath, ".png");

        var processStartInfo = new ProcessStartInfo
        {
            FileName = GetMermaidCliPath(),
            Arguments = $"-i \"{inputPath}\" -o \"{outputPath}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = processStartInfo };
        process.Start();
        process.WaitForExit();

        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        throw new Exception("Failed to generate Mermaid UML image output");
    }

    private string GenerateBrowserHtml(string inputPath)
    {
        var mermaidContent = File.ReadAllText(inputPath);
        var outputPath = Path.ChangeExtension(inputPath, ".html");

        var html = $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <title>Mermaid Diagram</title>
                <script src="https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.min.js"></script>
                <style>
                    body { 
                        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                        margin: 20px;
                        background: #f5f5f5;
                    }
                    .mermaid { 
                        background: white; 
                        padding: 20px; 
                        border-radius: 8px;
                        box-shadow: 0 2px 4px rgba(0,0,0,0.1);
                    }
                    h1 { color: #333; font-size: 1.2em; }
                </style>
            </head>
            <body>
                <h1>Mermaid Diagram</h1>
                <pre class="mermaid">
            {{mermaidContent}}
                </pre>
                <script>mermaid.initialize({startOnLoad:true, theme:'default'});</script>
            </body>
            </html>
            """;

        File.WriteAllText(outputPath, html);
        return outputPath;
    }

    private string GetMermaidCliPath()
    {
        if (_cachedMermaidCliPath != null)
            return _cachedMermaidCliPath;

        _cachedMermaidCliPath = FindExecutableInPath("mmdc") ?? "mmdc";
        return _cachedMermaidCliPath;
    }

    private bool IsMermaidCliAvailable()
    {
        if (_mermaidCliAvailable.HasValue)
            return _mermaidCliAvailable.Value;

        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = GetMermaidCliPath(),
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();
            process.WaitForExit(5000);

            _mermaidCliAvailable = process.ExitCode == 0;
        }
        catch
        {
            _mermaidCliAvailable = false;
        }

        return _mermaidCliAvailable.Value;
    }

    private static string? FindExecutableInPath(string executableName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        var paths = pathEnv.Split(Path.PathSeparator);
        var extensions = new[] { "", ".cmd", ".exe", ".bat" };

        foreach (var path in paths)
        {
            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(path, executableName + ext);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return null;
    }
}
