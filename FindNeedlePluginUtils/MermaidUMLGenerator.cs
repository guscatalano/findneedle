using System;
using System.Diagnostics;
using System.IO;
using FindNeedlePluginLib;
using FindNeedlePluginUtils.DependencyInstaller;

namespace FindNeedlePluginUtils;

public class MermaidUMLGenerator : IUMLGenerator
{
    private static string? _cachedMermaidCliPath = null;
    private static string? _cachedNodePath = null;
    private bool? _mermaidCliAvailable = null;
    private static MermaidInstaller? _installer = null;

    private static MermaidInstaller Installer => _installer ??= new MermaidInstaller();

    /// <summary>
    /// Clears cached paths. Call after installing dependencies.
    /// </summary>
    public static void ClearCache()
    {
        _cachedMermaidCliPath = null;
        _cachedNodePath = null;
        _installer = null;
    }

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
        var mmdcPath = GetMermaidCliPath();

        if (mmdcPath == null)
            throw new InvalidOperationException("Mermaid CLI not found. Please install via Diagram Tools page.");

        var nodePath = GetNodePath();
        var nodeDir = nodePath != null ? Path.GetDirectoryName(nodePath) : null;

        Logger.Instance.Log($"[MermaidUMLGenerator] Using mmdc at: {mmdcPath}");
        Logger.Instance.Log($"[MermaidUMLGenerator] Node directory: {nodeDir}");

        var arguments = $"-i \"{inputPath}\" -o \"{outputPath}\"";

        // For packaged apps, use PackagedAppCommandRunner to escape the AppContainer sandbox
        if (PackagedAppCommandRunner.IsPackagedApp)
        {
            Logger.Instance.Log($"[MermaidUMLGenerator] Running via PackagedAppCommandRunner (packaged app)");
            
            try
            {
                // Pass the Node directory as a PATH addition so mmdc.cmd can find node.exe
                var pathAdditions = nodeDir != null ? new[] { nodeDir } : null;
                var exitCode = PackagedAppCommandRunner.RunCommand(mmdcPath, arguments, nodeDir ?? Path.GetDirectoryName(mmdcPath)!, 60000, pathAdditions);
                Logger.Instance.Log($"[MermaidUMLGenerator] Process exit code: {exitCode}");
                
                if (File.Exists(outputPath))
                {
                    Logger.Instance.Log($"[MermaidUMLGenerator] Successfully generated: {outputPath}");
                    return outputPath;
                }
                
                throw new Exception($"Failed to generate Mermaid UML image output (exit code {exitCode})");
            }
            catch (TimeoutException)
            {
                throw new Exception("Mermaid CLI timed out after 60 seconds");
            }
        }
        else
        {
            // For unpackaged apps, run mmdc directly
            Logger.Instance.Log($"[MermaidUMLGenerator] Starting process directly (unpackaged app)...");
            
            var processStartInfo = new ProcessStartInfo
            {
                FileName = mmdcPath,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = nodeDir // Set working directory to Node folder
            };

            // Set PATH to include our Node installation
            if (nodeDir != null)
            {
                var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
                processStartInfo.Environment["PATH"] = nodeDir + ";" + currentPath;
            }

            using var process = new Process { StartInfo = processStartInfo };
            
            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"[MermaidUMLGenerator] Failed to start mmdc process: {ex.Message}");
                throw new Exception($"Failed to start Mermaid CLI: {ex.Message}", ex);
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Logger.Instance.Log($"[MermaidUMLGenerator] Process exit code: {process.ExitCode}");
            if (!string.IsNullOrWhiteSpace(stdout))
                Logger.Instance.Log($"[MermaidUMLGenerator] stdout: {stdout}");
            if (!string.IsNullOrWhiteSpace(stderr))
                Logger.Instance.Log($"[MermaidUMLGenerator] stderr: {stderr}");

            if (File.Exists(outputPath))
            {
                Logger.Instance.Log($"[MermaidUMLGenerator] Successfully generated: {outputPath}");
                return outputPath;
            }

            throw new Exception($"Failed to generate Mermaid UML image output: {stderr}");
        }
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

    private string? GetMermaidCliPath()
    {
        if (_cachedMermaidCliPath != null)
        {
            Logger.Instance.Log($"[MermaidUMLGenerator] Using cached mmdc path: {_cachedMermaidCliPath}");
            return _cachedMermaidCliPath;
        }

        // ONLY use our portable installation to avoid system Node issues
        var installerPath = Installer.GetMmdcPath();
        if (installerPath != null)
        {
            Logger.Instance.Log($"[MermaidUMLGenerator] Found mmdc via installer: {installerPath}");
            _cachedMermaidCliPath = installerPath;
            return _cachedMermaidCliPath;
        }

        Logger.Instance.Log($"[MermaidUMLGenerator] No portable Mermaid CLI installation found. Please install via Diagram Tools page.");
        return null;
    }

    private string? GetNodePath()
    {
        if (_cachedNodePath != null)
            return _cachedNodePath;

        // ONLY use our portable installation
        var installerPath = Installer.GetNodePath();
        if (installerPath != null)
        {
            _cachedNodePath = installerPath;
            return _cachedNodePath;
        }

        return null;
    }

    private bool IsMermaidCliAvailable()
    {
        if (_mermaidCliAvailable.HasValue)
            return _mermaidCliAvailable.Value;

        // Only check our portable installation
        _mermaidCliAvailable = Installer.IsInstalled();
        Logger.Instance.Log($"[MermaidUMLGenerator] IsMermaidCliAvailable: {_mermaidCliAvailable.Value}");
        return _mermaidCliAvailable.Value;
    }
}
