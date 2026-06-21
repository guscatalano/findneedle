using System;
using System.Diagnostics;
using System.IO;
using FindNeedlePluginLib;
using FindNeedleCoreUtils;
using FindNeedleToolInstallers;

namespace FindNeedleUmlDsl.MermaidUML;

public class MermaidUMLGenerator : IUMLGenerator
{
    private static string? _cachedMermaidCliPath = null;
    private static string? _cachedNodePath = null;
    private bool? _mermaidCliAvailable = null;

    private readonly IMermaidInstaller? _installer;

    public MermaidUMLGenerator(IMermaidInstaller? installer = null)
    {
        _installer = installer;
    }

    public void ClearCache()
    {
        _cachedMermaidCliPath = null;
        _cachedNodePath = null;
    }

    public string Name => "Mermaid";
    public string InputFileExtension => ".mmd";

    public UmlOutputType[] SupportedOutputTypes => IsSupported(UmlOutputType.ImageFile)
        ? [UmlOutputType.ImageFile, UmlOutputType.Browser]
        : IsSupported(UmlOutputType.Browser) ? [UmlOutputType.Browser] : [];

    public string GenerateUML(string inputPath, UmlOutputType outputType = UmlOutputType.ImageFile)
    {
        if (!File.Exists(inputPath))
            throw new Exception("Invalid input file path");

        if (!IsSupported(outputType))
            throw new Exception($"Output type '{outputType}' is not supported. Mermaid CLI (mmdc) is required for image generation.");

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
            UmlOutputType.ImageFile => IsMermaidCliAvailable(),
            UmlOutputType.Browser => true,
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

        if (nodeDir == null && mmdcPath != null)
        {
            try
            {
                var binDir = Path.GetDirectoryName(mmdcPath)!;
                var installRoot = Path.GetFullPath(Path.Combine(binDir, "..", ".."));
                var candidateNode = Path.Combine(installRoot, "node", "node.exe");
                if (File.Exists(candidateNode))
                {
                    nodeDir = Path.GetDirectoryName(candidateNode);
                    Logger.Instance.Log($"[MermaidUMLGenerator] Located bundled node at: {candidateNode}");
                }
                else if (Directory.Exists(installRoot))
                {
                    var found = Directory.EnumerateFiles(installRoot, "node.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (!string.IsNullOrEmpty(found))
                    {
                        nodeDir = Path.GetDirectoryName(found);
                        Logger.Instance.Log($"[MermaidUMLGenerator] Located node under install root: {found}");
                    }
                }
            }
            catch { }
        }

        Logger.Instance.Log($"[MermaidUMLGenerator] Using mmdc at: {mmdcPath}");
        try { Console.WriteLine($"[MermaidUMLGenerator] Using mmdc at: {mmdcPath}"); } catch { }
        Logger.Instance.Log($"[MermaidUMLGenerator] Node directory: {nodeDir}");
        try { Console.WriteLine($"[MermaidUMLGenerator] Node directory: {nodeDir}"); } catch { }

        // Announce which input/output pair is being processed so console users can trace progress
        try { Console.WriteLine($"[MermaidUMLGenerator] Processing: {inputPath} -> {outputPath}"); } catch { }

        // Use the original input file for mmdc by default (no truncation).
        var inputPathForMmdc = inputPath;

        // Sanitize .mmd input to remove accidental numbered dumps or embedded log lines that start with
        // a timestamp or numbered prefix which break the mermaid parser (it expects diagram tokens).
        try
        {
            var lines = File.ReadAllLines(inputPathForMmdc);
            var cleaned = new List<string>(lines.Length);
            foreach (var ln in lines)
            {
                var t = ln.TrimStart();
                // Skip numbered dump lines like "001: ..." or "12: ..."
                if (System.Text.RegularExpressions.Regex.IsMatch(t, "^\\d{1,3}:\\s"))
                    continue;
                // Skip common timestamp/log prefixes like "[2026-..."
                if (System.Text.RegularExpressions.Regex.IsMatch(t, "^\\[\\d{4}-\\d{2}-\\d{2}"))
                    continue;
                cleaned.Add(ln);
            }

                if (cleaned.Count != lines.Length)
                {
                    var cleanPath = Path.Combine(Path.GetDirectoryName(inputPathForMmdc) ?? Path.GetTempPath(), Path.GetFileNameWithoutExtension(inputPathForMmdc) + ".clean.mmd");
                    File.WriteAllLines(cleanPath, cleaned);
                    inputPathForMmdc = cleanPath;
                    Logger.Instance.Log($"[MermaidUMLGenerator] Cleaned mermaid input written: {cleanPath}");
                    try { Console.WriteLine($"[MermaidUMLGenerator] Cleaned mermaid input written: {cleanPath}"); } catch { }
                }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[MermaidUMLGenerator] Failed to sanitize mermaid input: {ex.Message}");
            inputPathForMmdc = inputPath;
        }

        var arguments = $"-i \"{inputPathForMmdc}\" -o \"{outputPath}\"";
        // numbered dump feature removed; no environment-controlled numbered dump is produced

                if (PackagedAppCommandRunner.IsPackagedApp)
        {
            Logger.Instance.Log($"[MermaidUMLGenerator] Running via PackagedAppCommandRunner (packaged app)");
                    try { Console.WriteLine($"[MermaidUMLGenerator] Running via PackagedAppCommandRunner: {mmdcPath} {arguments}"); } catch { }
            try
            {
                var pathAdditions = nodeDir != null ? new[] { nodeDir } : null;
                var exitCode = PackagedAppCommandRunner.RunCommand(mmdcPath, arguments, nodeDir ?? Path.GetDirectoryName(mmdcPath)!, 60000, pathAdditions);
                Logger.Instance.Log($"[MermaidUMLGenerator] Process exit code: {exitCode}");
                        try { Console.WriteLine($"[MermaidUMLGenerator] Packaged runner exit code: {exitCode}"); } catch { }

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
            Logger.Instance.Log($"[MermaidUMLGenerator] Starting process directly (unpackaged app)...");
                try { Console.WriteLine($"[MermaidUMLGenerator] Starting process directly: {mmdcPath} {arguments}"); } catch { }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = mmdcPath,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = nodeDir
            };

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
            try { Console.WriteLine($"[MermaidUMLGenerator] Process exit code: {process.ExitCode}"); } catch { }
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                Logger.Instance.Log($"[MermaidUMLGenerator] stdout: {stdout}");
                try { Console.WriteLine($"[MermaidUMLGenerator] stdout: {stdout}"); } catch { }
            }
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Logger.Instance.Log($"[MermaidUMLGenerator] stderr: {stderr}");
                try { Console.WriteLine($"[MermaidUMLGenerator] stderr: {stderr}"); } catch { }
            }

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

        var mermaidJsPath = GetMermaidJsPath();
        string mermaidScript;
        
        if (mermaidJsPath != null)
        {
            var mermaidJsContent = File.ReadAllText(mermaidJsPath);
            mermaidScript = mermaidJsContent;
            Logger.Instance.Log($"[MermaidUMLGenerator] Using local mermaid.js: {mermaidJsPath}");
        }
        else
        {
            // Fallback to CDN version
            mermaidScript = "<script src=\"https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.min.js\"></script>";
            Logger.Instance.Log($"[MermaidUMLGenerator] Using CDN mermaid.js");
        }

        var html = $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <title>Mermaid Diagram</title>
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
                {{mermaidScript}}
            </head>
            <body>
                <h1>Mermaid Diagram</h1>
                <pre class="mermaid">
            {{mermaidContent}}
                </pre>
                <script>mermaid.initialize({startOnLoad:true, theme:'default', maxTextSize:5000000});</script>
            </body>
            </html>
            """;

        File.WriteAllText(outputPath, html);
        Logger.Instance.Log($"[MermaidUMLGenerator] Generated browser HTML: {outputPath}");
        return outputPath;
    }

    private string? GetMermaidJsPath()
    {
        var mermaidDir = PackagedAppPaths.MermaidDir;
        var possiblePaths = new[]
        {
            Path.Combine(mermaidDir, "node_modules", "mermaid", "dist", "mermaid.min.js"),
            Path.Combine(mermaidDir, "node_modules", "mermaid", "dist", "mermaid.js"),
            Path.Combine(mermaidDir, "node_modules", "@mermaid-js", "mermaid-cli", "node_modules", "mermaid", "dist", "mermaid.min.js"),
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                Logger.Instance.Log($"[MermaidUMLGenerator] Found mermaid.js at: {path}");
                return path;
            }
        }

        Logger.Instance.Log($"[MermaidUMLGenerator] mermaid.js not found in any expected location");
        return null;
    }

    private string? GetMermaidCliPath()
    {
        if (_cachedMermaidCliPath != null)
        {
            Logger.Instance.Log($"[MermaidUMLGenerator] Using cached mmdc path: {_cachedMermaidCliPath}");
            return _cachedMermaidCliPath;
        }
        // Ask installer first (if injected)
        try
        {
            var installerPath = _installer?.GetMmdcPath();
            if (!string.IsNullOrEmpty(installerPath))
            {
                Logger.Instance.Log($"[MermaidUMLGenerator] Found mmdc via installer: {installerPath}");
                _cachedMermaidCliPath = installerPath;
                return _cachedMermaidCliPath;
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[MermaidUMLGenerator] Installer GetMmdcPath threw: {ex.Message}");
        }

        // Try cached environment scan
        try
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (var p in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(p, "mmdc.cmd");
                    if (File.Exists(candidate))
                    {
                        _cachedMermaidCliPath = candidate;
                        Logger.Instance.Log($"[MermaidUMLGenerator] Found mmdc on PATH at: {candidate}");
                        return _cachedMermaidCliPath;
                    }
                    candidate = Path.Combine(p, "mmdc");
                    if (File.Exists(candidate))
                    {
                        _cachedMermaidCliPath = candidate;
                        Logger.Instance.Log($"[MermaidUMLGenerator] Found mmdc on PATH at: {candidate}");
                        return _cachedMermaidCliPath;
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[MermaidUMLGenerator] PATH scan failed: {ex.Message}");
        }

        Logger.Instance.Log($"[MermaidUMLGenerator] No portable Mermaid CLI installation found. Please install via Diagram Tools page or include installer.");
        return null;
    }

    private string? GetNodePath()
    {
        if (_cachedNodePath != null)
            return _cachedNodePath;

        var installerPath = _installer?.GetNodePath();
        if (!string.IsNullOrEmpty(installerPath) && File.Exists(installerPath))
        {
            _cachedNodePath = installerPath;
            return _cachedNodePath;
        }

        try
        {
            var mmdc = _installer?.GetMmdcPath();
            if (!string.IsNullOrEmpty(mmdc))
            {
                var binDir = Path.GetDirectoryName(mmdc)!;
                var candidates = new[]
                {
                    Path.Combine(binDir, "..", "..", "node", "node.exe"),
                    Path.Combine(binDir, "..", "node", "node.exe"),
                    Path.Combine(binDir, "..", "..", "..", "node", "node.exe"),
                    Path.Combine(Path.GetDirectoryName(binDir) ?? binDir, "node.exe"),
                };

                foreach (var cand in candidates)
                {
                    try
                    {
                        var full = Path.GetFullPath(cand);
                        if (File.Exists(full))
                        {
                            _cachedNodePath = full;
                            return _cachedNodePath;
                        }
                    }
                    catch { }
                }

                try
                {
                    var installRoot = Path.GetFullPath(Path.Combine(binDir, "..", ".."));
                    if (Directory.Exists(installRoot))
                    {
                        var found = Directory.EnumerateFiles(installRoot, "node.exe", SearchOption.AllDirectories).FirstOrDefault();
                        if (!string.IsNullOrEmpty(found) && File.Exists(found))
                        {
                            _cachedNodePath = found;
                            return _cachedNodePath;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }

        return null;
    }

    private bool IsMermaidCliAvailable()
    {
        if (_mermaidCliAvailable.HasValue)
            return _mermaidCliAvailable.Value;
        try
        {
            var installed = false;
            try
            {
                installed = _installer?.IsInstalled() ?? false;
            }
            catch (Exception ex)
            {
                Logger.Instance.Log($"[MermaidUMLGenerator] Installer IsInstalled threw: {ex.Message}");
            }

            // Also check cached path and PATH
            if (!installed && !string.IsNullOrEmpty(_cachedMermaidCliPath) && File.Exists(_cachedMermaidCliPath))
                installed = true;

            _mermaidCliAvailable = installed;
            Logger.Instance.Log($"[MermaidUMLGenerator] IsMermaidCliAvailable: {_mermaidCliAvailable.Value} (installer={_installer != null}, cachedPath={_cachedMermaidCliPath})");
            return _mermaidCliAvailable.Value;
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"[MermaidUMLGenerator] IsMermaidCliAvailable check failed: {ex.Message}");
            _mermaidCliAvailable = false;
            return false;
        }
    }
}
