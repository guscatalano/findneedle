using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace FindNeedleToolInstallers;

public class MermaidInstaller : IDependencyInstaller, IMermaidInstaller
{
    private readonly string _installDirectory;

    public string DependencyName => "Mermaid CLI";
    public string Description => "Mermaid CLI (node + mermaid-cli)";

    public MermaidInstaller(string? installDirectory = null)
    {
        _installDirectory = installDirectory ?? GetDefaultInstallDir();
    }

    private static string GetDefaultInstallDir()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "FindNeedle", "Dependencies", "Mermaid");
    }

    public DependencyStatus GetStatus()
    {
        return new DependencyStatus
        {
            Name = DependencyName,
            Description = Description,
            IsInstalled = IsInstalled(),
            InstalledPath = GetMmdcPath(),
            InstallInstructions = "Install mermaid-cli into the packaged dependency folder"
        };
    }

    public bool IsInstalled()
    {
        var p = GetMmdcPath();
        return p != null && File.Exists(p);
    }

    public string? GetMmdcPath()
    {
        var path = Path.Combine(_installDirectory, "node_modules", ".bin", "mmdc.cmd");
        return File.Exists(path) ? path : null;
    }

    public string? GetNodePath()
    {
        var path = Path.Combine(_installDirectory, "node", "node.exe");
        return File.Exists(path) ? path : null;
    }

    public Task<InstallResult> InstallAsync(IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var log = new StringBuilder();
            void Log(string s)
            {
                try { log.AppendLine(s); } catch { }
            }

            try
            {
                Directory.CreateDirectory(_installDirectory);

                // Prefer system npm/node if available
                var systemNpm = FindExecutableInPath("npm") ?? FindExecutableInPath("npm.cmd");
                var systemNode = FindExecutableInPath("node") ?? FindExecutableInPath("node.exe");
                if (!string.IsNullOrEmpty(systemNpm) && !string.IsNullOrEmpty(systemNode))
                {
                    Log($"Using system npm: {systemNpm}");
                    progress?.Report(new InstallProgress { Status = "Using system npm to install mermaid-cli...", PercentComplete = 10, IsIndeterminate = true });

                    // On Windows, npm is a script file and cannot be executed directly without cmd.exe
                    ProcessStartInfo psi;
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        psi = new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c \"{systemNpm}\" install --prefix \"{_installDirectory}\" @mermaid-js/mermaid-cli", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                    }
                    else
                    {
                        psi = new ProcessStartInfo { FileName = systemNpm, Arguments = $"install --prefix \"{_installDirectory}\" @mermaid-js/mermaid-cli", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                    }

                    using var proc = Process.Start(psi)!;
                    var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                    var stderrTask = proc.StandardError.ReadToEndAsync();
                    while (!proc.WaitForExit(200))
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { proc.Kill(true); } catch { }
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                    }
                    var stdout = await stdoutTask;
                    var stderr = await stderrTask;
                    Log(stdout);
                    Log(stderr);
                    if (proc.ExitCode != 0)
                        return InstallResult.Failed($"npm install failed (system): {stderr}\n{log}");

                    var mmdc = GetMmdcPath();
                    if (mmdc == null)
                    {
                        var alt = Path.Combine(_installDirectory, "node_modules", ".bin", "mmdc.cmd");
                        if (File.Exists(alt)) mmdc = alt;
                    }
                    if (mmdc == null)
                        return InstallResult.Failed($"mermaid-cli installed but mmdc not found.\n{log}");

                    progress?.Report(new InstallProgress { Status = "mermaid-cli installed (system)", PercentComplete = 100, IsIndeterminate = false });
                    return InstallResult.Succeeded(mmdc);
                }

                // If not Windows, require system npm/node (bundled installer currently supports Windows only)
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return InstallResult.Failed("Bundled Node installer is only implemented for Windows. Please install Node.js and npm on your system.");
                }

                // Bundle Node for Windows
                var bundledNode = GetBundledNodePath();
                if (bundledNode == null)
                {
                    progress?.Report(new InstallProgress { Status = "Downloading portable Node runtime...", PercentComplete = 5, IsIndeterminate = true });

                    var nodeVersion = "v18.20.2"; // pinned
                    var nodeFileName = $"node-{nodeVersion}-win-x64.zip";
                    var nodeUrl = $"https://nodejs.org/dist/{nodeVersion}/{nodeFileName}";

                    var http = new HttpClient();
                    var tmp = Path.Combine(Path.GetTempPath(), nodeFileName);
                    var attempts = 0;
                    var success = false;
                    while (attempts++ < 3 && !success)
                    {
                        try
                        {
                            Log($"Downloading Node from {nodeUrl} (attempt {attempts})");
                            using var resp = await http.GetAsync(nodeUrl, cancellationToken);
                            resp.EnsureSuccessStatusCode();
                            await using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write);
                            await resp.Content.CopyToAsync(fs, cancellationToken);
                            success = true;
                        }
                        catch (Exception ex)
                        {
                            Log($"Node download attempt {attempts} failed: {ex.Message}");
                            if (attempts >= 3) return InstallResult.Failed($"Failed to download Node runtime after {attempts} attempts: {ex.Message}\n{log}");
                            await Task.Delay(1000, cancellationToken);
                        }
                    }

                    progress?.Report(new InstallProgress { Status = "Extracting Node runtime...", PercentComplete = 30, IsIndeterminate = true });
                    var extractDir = Path.Combine(Path.GetTempPath(), "findneedle_node_extract");
                    try
                    {
                        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                        ZipFile.ExtractToDirectory(tmp, extractDir);
                    }
                    catch (Exception ex)
                    {
                        return InstallResult.Failed($"Failed to extract Node runtime: {ex.Message}\n{log}");
                    }

                    var extractedRoot = Directory.GetDirectories(extractDir).FirstOrDefault() ?? extractDir;
                    var targetNodeDir = Path.Combine(_installDirectory, "node");
                    try
                    {
                        if (Directory.Exists(targetNodeDir)) Directory.Delete(targetNodeDir, true);
                        Directory.CreateDirectory(Path.GetDirectoryName(targetNodeDir) ?? _installDirectory);
                        Directory.Move(extractedRoot, targetNodeDir);
                    }
                    catch (IOException)
                    {
                        CopyDirectory(extractedRoot, targetNodeDir);
                    }
                    finally
                    {
                        try { File.Delete(tmp); } catch { }
                        try { Directory.Delete(extractDir, true); } catch { }
                    }

                    bundledNode = GetBundledNodePath();
                    if (bundledNode == null)
                        return InstallResult.Failed($"Failed to install bundled Node runtime.\n{log}");
                }

                progress?.Report(new InstallProgress { Status = "Installing mermaid-cli via bundled npm...", PercentComplete = 50, IsIndeterminate = true });

                var nodeRoot = Path.GetDirectoryName(bundledNode)!;
                string? npmCmd = Path.Combine(nodeRoot, "npm.cmd");
                string? nodeExe = bundledNode;
                string? npmCliJs = Path.Combine(nodeRoot, "node_modules", "npm", "bin", "npm-cli.js");

                ProcessStartInfo psiBundled;
                if (File.Exists(npmCmd))
                {
                    psiBundled = new ProcessStartInfo { FileName = npmCmd, Arguments = $"install --prefix \"{_installDirectory}\" @mermaid-js/mermaid-cli", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                }
                else if (File.Exists(npmCliJs) && File.Exists(nodeExe))
                {
                    psiBundled = new ProcessStartInfo { FileName = nodeExe, Arguments = $"\"{npmCliJs}\" install --prefix \"{_installDirectory}\" @mermaid-js/mermaid-cli", RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                }
                else
                {
                    return InstallResult.Failed($"npm not found in bundled node runtime.\n{log}");
                }

                // Add bundled node directory to PATH so that npm post-install scripts can find node.exe
                var existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                psiBundled.Environment["PATH"] = $"{nodeRoot}{Path.PathSeparator}{existingPath}";

                using var proc2 = Process.Start(psiBundled)!;
                var stdout2 = proc2.StandardOutput.ReadToEndAsync();
                var stderr2 = proc2.StandardError.ReadToEndAsync();
                while (!proc2.WaitForExit(200))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        try { proc2.Kill(true); } catch { }
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                var outText = await stdout2;
                var errText = await stderr2;
                Log(outText);
                Log(errText);

                if (proc2.ExitCode != 0)
                {
                    return InstallResult.Failed($"npm install failed (bundled): {errText}\n{log}");
                }

                var mmdcPath = GetMmdcPath();
                if (mmdcPath == null)
                {
                    var alt = Path.Combine(_installDirectory, "node_modules", ".bin", "mmdc.cmd");
                    if (File.Exists(alt)) mmdcPath = alt;
                }

                if (mmdcPath == null)
                {
                    return InstallResult.Failed($"mermaid-cli installed but mmdc binary not found after installation.\n{log}");
                }

                // Create a wrapper script that sets up the environment for mmdc
                var wrapperPath = CreateMermaidWrapper(mmdcPath);
                if (wrapperPath == null)
                {
                    Log("Warning: Failed to create mmdc wrapper, using direct path");
                    wrapperPath = mmdcPath;
                }

                progress?.Report(new InstallProgress { Status = "mermaid-cli installed", PercentComplete = 100, IsIndeterminate = false });
                return InstallResult.Succeeded(wrapperPath);
            }
            catch (OperationCanceledException)
            {
                return InstallResult.Failed("Installation cancelled");
            }
            catch (Exception ex)
            {
                return InstallResult.Failed(ex.Message + "\n" + ex.StackTrace);
            }
        }, cancellationToken);
    }

    private static string? FindExecutableInPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var candidate = Path.Combine(dir.Trim(), name);
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    private string? GetBundledNodePath()
    {
        var nodeExe = Path.Combine(_installDirectory, "node", "node.exe");
        return File.Exists(nodeExe) ? nodeExe : null;
    }

    private string? CreateMermaidWrapper(string mmdcPath)
    {
        try
        {
            var nodeDir = Path.Combine(_installDirectory, "node");
            var wrapperPath = Path.Combine(_installDirectory, "node_modules", ".bin", "mmdc-wrapper.cmd");

            var wrapperContent = $"""
@echo off
setlocal enabledelayedexpansion
set NODE_PATH={nodeDir}
set PATH={nodeDir};!PATH!
call "{mmdcPath}" %*
""";

            File.WriteAllText(wrapperPath, wrapperContent);
            return wrapperPath;
        }
        catch
        {
            return null;
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dirPath.Replace(sourceDir, destinationDir));
        }
        foreach (var newPath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
        {
            File.Copy(newPath, newPath.Replace(sourceDir, destinationDir), true);
        }
    }
}
