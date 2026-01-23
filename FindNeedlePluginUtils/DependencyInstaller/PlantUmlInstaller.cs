using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using FindNeedlePluginLib;

namespace FindNeedlePluginUtils.DependencyInstaller;

/// <summary>
/// Installs PlantUML and its Java dependency to a local directory.
/// </summary>
public class PlantUmlInstaller : IDependencyInstaller
{
    private const string PlantUmlVersion = "1.2025.2";
    private const string PlantUmlJarUrl = "https://github.com/plantuml/plantuml/releases/download/v1.2025.2/plantuml-mit-1.2025.2.jar";
    private const string PlantUmlJarName = "plantuml.jar";
    
    // Microsoft OpenJDK - better compatibility with Windows packaged apps
    private const string JavaVersion = "21.0.5";
    private const string JavaWindowsX64Url = "https://aka.ms/download-jdk/microsoft-jdk-21.0.5-windows-x64.zip";
    
    private readonly string _installDirectory;
    private readonly HttpClient _httpClient;

    public string DependencyName => "PlantUML";
    public string Description => "PlantUML diagram generator (includes portable Java runtime)";

    public PlantUmlInstaller(string? installDirectory = null, HttpClient? httpClient = null)
    {
        _installDirectory = installDirectory ?? GetDefaultInstallDirectory();
        _httpClient = httpClient ?? new HttpClient();
        Log($"PlantUmlInstaller initialized with directory: {_installDirectory}");
    }

    private static void Log(string message) => Logger.Instance.Log($"[PlantUmlInstaller] {message}");

    private static string GetDefaultInstallDirectory()
    {
        // Use standard LocalAppData - for packaged apps, file system virtualization
        // will transparently redirect to the package-specific location
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "FindNeedle", "Dependencies", "PlantUML");
    }

    public DependencyStatus GetStatus()
    {
        Log("Getting status...");
        var status = new DependencyStatus
        {
            Name = DependencyName,
            Description = Description,
            IsInstalled = IsInstalled(),
            InstallInstructions = "Click Install to download PlantUML and a portable Java runtime (~60MB)"
        };

        if (status.IsInstalled)
        {
            status.InstalledPath = GetPlantUmlJarPath();
            status.InstalledVersion = GetVersion();
            Log($"Status: Installed at {status.InstalledPath}, version {status.InstalledVersion}");
        }
        else
        {
            Log("Status: Not installed");
        }

        return status;
    }

    public bool IsInstalled()
    {
        var jarPath = GetPlantUmlJarPath();
        var javaPath = GetJavaPath();
        var jliPath = GetJliDllPath();

        Log($"IsInstalled check: jarPath={jarPath ?? "null"}, javaPath={javaPath ?? "null"}, jliPath={jliPath ?? "null"}");
        return File.Exists(jarPath) && File.Exists(javaPath) && File.Exists(jliPath);
    }

    public string? GetPlantUmlJarPath()
    {
        var path = Path.Combine(_installDirectory, PlantUmlJarName);
        return File.Exists(path) ? path : null;
    }

    public string? GetJavaPath()
    {
        var javaDir = Path.Combine(_installDirectory, "jre");
        var javaExe = Path.Combine(javaDir, "bin", "java.exe");
        
        if (File.Exists(javaExe))
        {
            Log($"Found java.exe at: {javaExe}");
            return javaExe;
        }

        // Check subdirectories (JRE extracts with version folder)
        if (Directory.Exists(javaDir))
        {
            foreach (var dir in Directory.GetDirectories(javaDir))
            {
                var nestedJava = Path.Combine(dir, "bin", "java.exe");
                if (File.Exists(nestedJava))
                {
                    Log($"Found java.exe at: {nestedJava}");
                    return nestedJava;
                }
            }
        }

        Log($"Java not found in: {javaDir}");
        return null;
    }

    /// <summary>
    /// Gets the path to jli.dll which is required for Java to run.
    /// </summary>
    public string? GetJliDllPath()
    {
        var javaPath = GetJavaPath();
        if (javaPath == null) return null;

        var javaBinDir = Path.GetDirectoryName(javaPath);
        if (javaBinDir == null) return null;

        // jli.dll is typically in the bin folder alongside java.exe
        var jliPath = Path.Combine(javaBinDir, "jli.dll");
        if (File.Exists(jliPath))
        {
            Log($"Found jli.dll at: {jliPath}");
            return jliPath;
        }

        // Some JRE distributions put it in bin/server or other subfolders
        var serverJli = Path.Combine(javaBinDir, "server", "jli.dll");
        if (File.Exists(serverJli))
        {
            Log($"Found jli.dll at: {serverJli}");
            return serverJli;
        }

        Log($"jli.dll not found in: {javaBinDir}");
        return null;
    }

    private string? GetVersion()
    {
        try
        {
            // Don't try to run Java if not properly installed
            if (!IsInstalled())
            {
                Log("GetVersion: Not installed, skipping version check");
                return null;
            }

            // Return the known version we downloaded
            Log($"GetVersion: Returning known PlantUML version {PlantUmlVersion} (Java {JavaVersion})");
            return $"{PlantUmlVersion} (Java {JavaVersion})";
        }
        catch (Exception ex)
        {
            Log($"GetVersion failed: {ex.Message}");
            return PlantUmlVersion;
        }
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

            // Always reinstall Java to ensure clean installation with properly named folders
            var jreDir = Path.Combine(_installDirectory, "jre");
            if (Directory.Exists(jreDir))
            {
                Log("Removing existing JRE directory for clean reinstall...");
                progress?.Report(new InstallProgress { Status = "Removing old Java installation...", PercentComplete = 5 });
                try
                {
                    Directory.Delete(jreDir, true);
                }
                catch (Exception ex)
                {
                    Log($"Warning: Could not delete existing JRE directory: {ex.Message}");
                }
            }

            // Install Java JRE
            Log("Downloading Java...");
            progress?.Report(new InstallProgress { Status = "Downloading Java runtime...", PercentComplete = 10 });
            await DownloadAndExtractJavaAsync(progress, cancellationToken);

            // Install PlantUML JAR
            Log("Downloading PlantUML JAR...");
            progress?.Report(new InstallProgress { Status = "Downloading PlantUML...", PercentComplete = 70 });
            await DownloadPlantUmlJarAsync(progress, cancellationToken);

            // Clear cached paths so they get re-detected
            PlantUMLGenerator.ClearCache();

            progress?.Report(new InstallProgress { Status = "Installation complete!", PercentComplete = 100 });

            var jarPath = GetPlantUmlJarPath();
            Log($"Installation complete! JAR path: {jarPath}");
            return InstallResult.Succeeded(jarPath!);
        }
        catch (Exception ex)
        {
            Log($"Installation failed with exception: {ex}");
            return InstallResult.Failed($"Installation failed: {ex.Message}");
        }
    }

    private async Task DownloadAndExtractJavaAsync(
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || 
            RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                "Automatic Java installation only supports Windows x64. Please install Java manually.");
        }

        var zipPath = Path.Combine(_installDirectory, "jre.zip");
        var jreDir = Path.Combine(_installDirectory, "jre");

        try
        {
            // Download
            Log($"Downloading Java from: {JavaWindowsX64Url}");
            progress?.Report(new InstallProgress { Status = "Downloading Java runtime...", PercentComplete = 10 });
            
            using (var response = await _httpClient.GetAsync(JavaWindowsX64Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                var contentLength = response.Content.Headers.ContentLength;
                Log($"Download response received, content length: {contentLength}");
                
                await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream, cancellationToken);
                Log($"Download complete, file size: {new FileInfo(zipPath).Length}");
            }

            // Extract
            Log($"Extracting Java to: {jreDir}");
            progress?.Report(new InstallProgress { Status = "Extracting Java runtime...", PercentComplete = 50 });
            
            if (Directory.Exists(jreDir))
            {
                Log("Removing existing jre directory");
                Directory.Delete(jreDir, true);
            }
            
            ZipFile.ExtractToDirectory(zipPath, jreDir, overwriteFiles: true);
            
            // List what was extracted
            var extractedDirs = Directory.GetDirectories(jreDir);
            Log($"Extracted {extractedDirs.Length} directories: {string.Join(", ", extractedDirs.Select(Path.GetFileName))}");

            // Rename folders with special characters (like +) to avoid path issues
            progress?.Report(new InstallProgress { Status = "Configuring Java runtime...", PercentComplete = 55 });
            RenameProblematicFolders(jreDir);

            // Verify critical files exist
            progress?.Report(new InstallProgress { Status = "Verifying Java installation...", PercentComplete = 60 });
            VerifyJavaInstallation();
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

    /// <summary>
    /// Renames folders containing special characters (like +) that can cause issues with command-line tools.
    /// </summary>
    private void RenameProblematicFolders(string directory)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(directory))
            {
                var dirName = Path.GetFileName(dir);
                
                // Check if folder name contains problematic characters
                if (dirName.Contains('+') || dirName.Contains(' '))
                {
                    // Create a safe folder name by replacing problematic characters
                    var safeName = dirName.Replace("+", "_").Replace(" ", "_");
                    var newPath = Path.Combine(directory, safeName);
                    
                    if (!Directory.Exists(newPath))
                    {
                        Log($"Renaming folder: {dirName} -> {safeName}");
                        Directory.Move(dir, newPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Warning: Could not rename folders: {ex.Message}");
            // Continue anyway - the original folders might still work
        }
    }

    /// <summary>
    /// Verifies that all critical Java files are present after extraction.
    /// </summary>
    private void VerifyJavaInstallation()
    {
        var javaPath = GetJavaPath();
        if (javaPath == null)
        {
            throw new InvalidOperationException("Java installation failed: java.exe not found after extraction.");
        }

        var javaBinDir = Path.GetDirectoryName(javaPath);
        Log($"Verifying Java installation in: {javaBinDir}");

        // Check for jli.dll
        var jliPath = GetJliDllPath();
        if (jliPath == null)
        {
            // List all DLLs in bin directory for debugging
            if (javaBinDir != null && Directory.Exists(javaBinDir))
            {
                var dlls = Directory.GetFiles(javaBinDir, "*.dll").Select(Path.GetFileName);
                Log($"DLLs in bin directory: {string.Join(", ", dlls)}");
            }
            throw new InvalidOperationException("Java installation incomplete: jli.dll not found. This DLL is required for Java to run.");
        }

        // Check for jvm.dll (another critical file)
        var jvmPaths = new[]
        {
            Path.Combine(javaBinDir!, "server", "jvm.dll"),
            Path.Combine(javaBinDir!, "client", "jvm.dll"),
            Path.Combine(javaBinDir!, "jvm.dll")
        };

        var jvmPath = jvmPaths.FirstOrDefault(File.Exists);
        if (jvmPath != null)
        {
            Log($"Found jvm.dll at: {jvmPath}");
        }
        else
        {
            Log("Warning: jvm.dll not found, but continuing...");
        }

        Log("Java installation verified successfully!");
    }

    private async Task DownloadPlantUmlJarAsync(
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var jarPath = Path.Combine(_installDirectory, PlantUmlJarName);

        Log($"Downloading PlantUML JAR from: {PlantUmlJarUrl}");
        progress?.Report(new InstallProgress { Status = "Downloading PlantUML jar...", PercentComplete = 80 });

        using var response = await _httpClient.GetAsync(PlantUmlJarUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var contentLength = response.Content.Headers.ContentLength;
        Log($"Download response received, content length: {contentLength}");

        await using var fileStream = new FileStream(jarPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fileStream, cancellationToken);
        
        Log($"PlantUML JAR downloaded, file size: {new FileInfo(jarPath).Length}");
    }
}
