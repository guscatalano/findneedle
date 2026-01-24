using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using FindNeedlePluginLib;

namespace FindNeedlePluginUtils.DependencyInstaller;

/// <summary>
/// Installs Mermaid CLI and its Node.js dependency to a local directory.
/// </summary>
public class MermaidInstaller : IDependencyInstaller
{
    // Node.js portable version
    private const string NodeWindowsX64Url = "https://nodejs.org/dist/v22.12.0/node-v22.12.0-win-x64.zip";
    
    private readonly string _installDirectory;
    private readonly HttpClient _httpClient;

    public string DependencyName => "Mermaid CLI";
    public string Description => "Mermaid diagram generator (includes portable Node.js runtime)";

    public MermaidInstaller(string? installDirectory = null, HttpClient? httpClient = null)
    {
        _installDirectory = installDirectory ?? PackagedAppPaths.MermaidDir;
        _httpClient = httpClient ?? new HttpClient();
        Log($"MermaidInstaller initialized with directory: {_installDirectory}");
    }

    private static void Log(string message) => Logger.Instance.Log($"[MermaidInstaller] {message}");

    public DependencyStatus GetStatus()
    {
        Log("Getting status...");
        var status = new DependencyStatus
        {
            Name = DependencyName,
            Description = Description,
            IsInstalled = IsInstalled(),
            InstallInstructions = "Click Install to download Mermaid CLI and a portable Node.js runtime (~80MB)"
        };


        if (status.IsInstalled)
        {
            status.InstalledPath = GetMmdcPath();
            // Don't fetch version here - it's slow. Use GetVersionAsync() instead.
            status.InstalledVersion = null;
            Log($"Status: Installed at {status.InstalledPath}");
        }
        else
        {
            Log("Status: Not installed");
        }

        return status;
    }

    public bool IsInstalled()
    {
        var mmdcPath = GetMmdcPath();
        var installed = mmdcPath != null && File.Exists(mmdcPath);
        Log($"IsInstalled check: mmdcPath={mmdcPath ?? "null"}, exists={installed}");
        return installed;
    }

    public string? GetMmdcPath()
    {
        var mmdcCmd = Path.Combine(_installDirectory, "node_modules", ".bin", "mmdc.cmd");
        Log($"Looking for mmdc.cmd at: {mmdcCmd}");
        return File.Exists(mmdcCmd) ? mmdcCmd : null;
    }

    public string? GetNodePath()
    {
        var nodeDir = Path.Combine(_installDirectory, "node");
        Log($"Looking for Node.js in: {nodeDir}");
        
        // Check for node.exe directly or in subdirectories
        var nodeExe = Path.Combine(nodeDir, "node.exe");
        if (File.Exists(nodeExe))
        {
            Log($"Found node.exe at: {nodeExe}");
            return nodeExe;
        }

        // Check subdirectories (Node extracts with version folder)
        if (Directory.Exists(nodeDir))
        {
            foreach (var dir in Directory.GetDirectories(nodeDir))
            {
                var nestedNode = Path.Combine(dir, "node.exe");
                if (File.Exists(nestedNode))
                {
                    Log($"Found node.exe at: {nestedNode}");
                    return nestedNode;
                }
            }
        }

        Log("Node.exe not found");
        return null;
    }

    public string? GetNpmPath()
    {
        var nodePath = GetNodePath();
        if (nodePath == null) return null;
        
        var nodeDir = Path.GetDirectoryName(nodePath);
        var npmCmd = Path.Combine(nodeDir!, "npm.cmd");
        
        Log($"Looking for npm.cmd at: {npmCmd}, exists={File.Exists(npmCmd)}");
        return File.Exists(npmCmd) ? npmCmd : null;
    }


    private string? GetVersion()
    {
        try
        {
            if (!IsInstalled())
            {
                Log("GetVersion: Not installed, skipping version check");
                return null;
            }

            // Read version from package.json instead of running mmdc --version
            // This avoids spawning processes and popup windows
            var packageJsonPath = Path.Combine(_installDirectory, "node_modules", "@mermaid-js", "mermaid-cli", "package.json");
            
            if (!File.Exists(packageJsonPath))
            {
                Log($"GetVersion: package.json not found at {packageJsonPath}");
                return null;
            }

            var json = File.ReadAllText(packageJsonPath);
            var version = ParseVersionFromPackageJson(json);
            
            Log($"GetVersion: Found version {version} from package.json");
            return version;
        }
        catch (Exception ex)
        {
            Log($"GetVersion failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses the version from package.json content.
    /// </summary>
    private static string? ParseVersionFromPackageJson(string json)
    {
        // Simple parsing - look for "version": "x.y.z"
        // We avoid full JSON parsing to keep it lightweight
        var versionPattern = "\"version\"";
        var idx = json.IndexOf(versionPattern, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        // Find the colon after "version"
        var colonIdx = json.IndexOf(':', idx + versionPattern.Length);
        if (colonIdx < 0) return null;

        // Find the opening quote
        var startQuote = json.IndexOf('"', colonIdx + 1);
        if (startQuote < 0) return null;

        // Find the closing quote
        var endQuote = json.IndexOf('"', startQuote + 1);
        if (endQuote < 0) return null;

        return json.Substring(startQuote + 1, endQuote - startQuote - 1);
    }

    /// <summary>
    /// Gets the installed Mermaid CLI version asynchronously.
    /// Reads from package.json - fast and doesn't spawn processes.
    /// </summary>
    public async Task<string?> GetVersionAsync()
    {
        if (!IsInstalled())
            return null;

        return await Task.Run(() => GetVersion());
    }

    public async Task<InstallResult> InstallAsync(
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Log("Starting installation...");
        try
        {
            Log($"Creating install directory: {_installDirectory}");
            Directory.CreateDirectory(_installDirectory);

            // Install Node.js if needed
            if (GetNodePath() == null)
            {
                Log("Node.js not found, downloading...");
                progress?.Report(new InstallProgress { Status = "Downloading Node.js runtime...", PercentComplete = 0 });
                await DownloadAndExtractNodeAsync(progress, cancellationToken);
            }
            else
            {
                Log("Node.js already installed, skipping download");
            }

            // Install mermaid-cli via npm
            Log("Installing Mermaid CLI via npm...");
            progress?.Report(new InstallProgress { Status = "Installing Mermaid CLI...", PercentComplete = 60 });
            await InstallMermaidCliAsync(progress, cancellationToken);

            progress?.Report(new InstallProgress { Status = "Installation complete!", PercentComplete = 100 });

            var mmdcPath = GetMmdcPath();
            Log($"Installation complete! mmdc path: {mmdcPath}");
            return InstallResult.Succeeded(mmdcPath!);
        }
        catch (Exception ex)
        {
            Log($"Installation failed with exception: {ex}");
            return InstallResult.Failed($"Installation failed: {ex.Message}");
        }
    }

    private async Task DownloadAndExtractNodeAsync(
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
            RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                "Automatic Node.js installation only supports Windows x64. Please install Node.js manually.");
        }

        var zipPath = Path.Combine(_installDirectory, "node.zip");
        var nodeDir = Path.Combine(_installDirectory, "node");

        try
        {
            // Download
            Log($"Downloading Node.js from: {NodeWindowsX64Url}");
            progress?.Report(new InstallProgress { Status = "Downloading Node.js...", PercentComplete = 10 });

            using (var response = await _httpClient.GetAsync(NodeWindowsX64Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                var contentLength = response.Content.Headers.ContentLength;
                Log($"Download response received, content length: {contentLength}");

                await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream, cancellationToken);
                Log($"Download complete, file size: {new FileInfo(zipPath).Length}");
            }

            // Extract
            Log($"Extracting Node.js to: {nodeDir}");
            progress?.Report(new InstallProgress { Status = "Extracting Node.js...", PercentComplete = 40 });

            if (Directory.Exists(nodeDir))
            {
                Log("Removing existing node directory");
                Directory.Delete(nodeDir, true);
            }

            ZipFile.ExtractToDirectory(zipPath, nodeDir, overwriteFiles: true);
            
            // List what was extracted
            var extractedDirs = Directory.GetDirectories(nodeDir);
            Log($"Extracted {extractedDirs.Length} directories: {string.Join(", ", extractedDirs.Select(Path.GetFileName))}");
        }
        finally
        {
            if (File.Exists(zipPath))
            {
                Log("Cleaning up zip file");
                File.Delete(zipPath);
            }
        }
    }

    private async Task InstallMermaidCliAsync(
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var npmPath = GetNpmPath();
        if (npmPath == null)
        {
            Log("ERROR: npm not found after Node.js extraction");
            throw new InvalidOperationException("npm not found. Node.js installation may have failed.");
        }

        var nodePath = GetNodePath();
        var nodeDir = Path.GetDirectoryName(nodePath);

        Log($"Running npm install in: {_installDirectory}");
        Log($"Using npm at: {npmPath}");
        Log($"Node directory for PATH: {nodeDir}");
        progress?.Report(new InstallProgress { Status = "Running npm install...", PercentComplete = 70, IsIndeterminate = true });

        // For packaged apps, use PackagedAppCommandRunner to run npm in the correct context
        if (PackagedAppCommandRunner.IsPackagedApp)
        {
            Log("Running npm via PackagedAppCommandRunner (packaged app)");
            // Pass the Node directory as a PATH addition so npm.cmd can find node.exe
            var pathAdditions = nodeDir != null ? new[] { nodeDir } : null;
            var exitCode = await Task.Run(() => 
                PackagedAppCommandRunner.RunCommand(npmPath, "install @mermaid-js/mermaid-cli", _installDirectory, 300000, pathAdditions), // 5 min timeout for npm
                cancellationToken);
            
            Log($"npm install exit code: {exitCode}");
            
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"npm install failed (exit code {exitCode})");
            }
        }
        else
        {
            // For unpackaged apps, run npm directly
            var psi = new ProcessStartInfo
            {
                FileName = npmPath,
                Arguments = "install @mermaid-js/mermaid-cli",
                WorkingDirectory = _installDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Ensure our Node is in PATH
            psi.Environment["PATH"] = nodeDir + ";" + Environment.GetEnvironmentVariable("PATH");

            using var process = new Process { StartInfo = psi };
            process.Start();

            // Read output asynchronously to prevent deadlock
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            Log($"npm install exit code: {process.ExitCode}");
            if (!string.IsNullOrWhiteSpace(output))
                Log($"npm stdout: {output}");
            if (!string.IsNullOrWhiteSpace(error))
                Log($"npm stderr: {error}");

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"npm install failed (exit code {process.ExitCode}): {error}");
            }
        }

        // Verify installation
        if (!IsInstalled())
        {
            Log("ERROR: mmdc.cmd not found after npm install");
            // List what files exist in node_modules
            var nodeModulesPath = Path.Combine(_installDirectory, "node_modules");
            if (Directory.Exists(nodeModulesPath))
            {
                var binPath = Path.Combine(nodeModulesPath, ".bin");
                if (Directory.Exists(binPath))
                {
                    var binFiles = Directory.GetFiles(binPath);
                    Log($"Files in node_modules/.bin: {string.Join(", ", binFiles.Select(Path.GetFileName))}");
                }
                else
                {
                    Log("node_modules/.bin directory does not exist");
                }
            }
            else
            {
                Log("node_modules directory does not exist");
            }
            throw new InvalidOperationException("mermaid-cli installation completed but mmdc was not found.");
        }
        
        Log("Mermaid CLI installation verified successfully");
    }
}
