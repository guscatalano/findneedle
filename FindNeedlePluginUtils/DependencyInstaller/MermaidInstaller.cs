using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;

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
        _installDirectory = installDirectory ?? GetDefaultInstallDirectory();
        _httpClient = httpClient ?? new HttpClient();
    }

    private static string GetDefaultInstallDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "FindNeedle", "Dependencies", "Mermaid");
    }

    public DependencyStatus GetStatus()
    {
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
            status.InstalledVersion = GetVersion();
        }

        return status;
    }

    public bool IsInstalled()
    {
        var mmdcPath = GetMmdcPath();
        return mmdcPath != null && File.Exists(mmdcPath);
    }

    public string? GetMmdcPath()
    {
        var mmdcCmd = Path.Combine(_installDirectory, "node_modules", ".bin", "mmdc.cmd");
        return File.Exists(mmdcCmd) ? mmdcCmd : null;
    }

    public string? GetNodePath()
    {
        var nodeDir = Path.Combine(_installDirectory, "node");
        
        // Check for node.exe directly or in subdirectories
        var nodeExe = Path.Combine(nodeDir, "node.exe");
        if (File.Exists(nodeExe))
            return nodeExe;

        // Check subdirectories (Node extracts with version folder)
        if (Directory.Exists(nodeDir))
        {
            foreach (var dir in Directory.GetDirectories(nodeDir))
            {
                var nestedNode = Path.Combine(dir, "node.exe");
                if (File.Exists(nestedNode))
                    return nestedNode;
            }
        }

        return null;
    }

    public string? GetNpmPath()
    {
        var nodePath = GetNodePath();
        if (nodePath == null) return null;
        
        var nodeDir = Path.GetDirectoryName(nodePath);
        var npmCmd = Path.Combine(nodeDir!, "npm.cmd");
        
        return File.Exists(npmCmd) ? npmCmd : null;
    }

    private string? GetVersion()
    {
        try
        {
            var mmdcPath = GetMmdcPath();
            if (mmdcPath == null) return null;

            var psi = new ProcessStartInfo
            {
                FileName = mmdcPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = _installDirectory
            };

            // Set PATH to include our Node installation
            var nodePath = GetNodePath();
            if (nodePath != null)
            {
                var nodeDir = Path.GetDirectoryName(nodePath);
                psi.Environment["PATH"] = nodeDir + ";" + Environment.GetEnvironmentVariable("PATH");
            }

            using var process = Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd();
            process?.WaitForExit(5000);

            return output?.Trim();
        }
        catch
        {
            return "Unknown";
        }
    }

    public async Task<InstallResult> InstallAsync(
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_installDirectory);

            // Install Node.js if needed
            if (GetNodePath() == null)
            {
                progress?.Report(new InstallProgress { Status = "Downloading Node.js runtime...", PercentComplete = 0 });
                await DownloadAndExtractNodeAsync(progress, cancellationToken);
            }

            // Install mermaid-cli via npm
            progress?.Report(new InstallProgress { Status = "Installing Mermaid CLI...", PercentComplete = 60 });
            await InstallMermaidCliAsync(progress, cancellationToken);

            progress?.Report(new InstallProgress { Status = "Installation complete!", PercentComplete = 100 });

            return InstallResult.Succeeded(GetMmdcPath()!);
        }
        catch (Exception ex)
        {
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
            progress?.Report(new InstallProgress { Status = "Downloading Node.js...", PercentComplete = 10 });

            using (var response = await _httpClient.GetAsync(NodeWindowsX64Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();

                await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }

            // Extract
            progress?.Report(new InstallProgress { Status = "Extracting Node.js...", PercentComplete = 40 });

            if (Directory.Exists(nodeDir))
                Directory.Delete(nodeDir, true);

            ZipFile.ExtractToDirectory(zipPath, nodeDir, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }

    private async Task InstallMermaidCliAsync(
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var npmPath = GetNpmPath();
        if (npmPath == null)
            throw new InvalidOperationException("npm not found. Node.js installation may have failed.");

        var nodePath = GetNodePath();
        var nodeDir = Path.GetDirectoryName(nodePath);

        progress?.Report(new InstallProgress { Status = "Running npm install...", PercentComplete = 70, IsIndeterminate = true });

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

        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"npm install failed: {error}");
        }

        // Verify installation
        if (!IsInstalled())
        {
            throw new InvalidOperationException("mermaid-cli installation completed but mmdc was not found.");
        }
    }
}
