using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace FindNeedlePluginUtils.DependencyInstaller;

/// <summary>
/// Installs PlantUML and its Java dependency to a local directory.
/// </summary>
public class PlantUmlInstaller : IDependencyInstaller
{
    private const string PlantUmlJarUrl = "https://github.com/plantuml/plantuml/releases/download/v1.2025.2/plantuml-mit-1.2025.2.jar";
    private const string PlantUmlJarName = "plantuml.jar";
    
    // Eclipse Temurin (Adoptium) portable JRE
    private const string JavaWindowsX64Url = "https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.5%2B11/OpenJDK21U-jre_x64_windows_hotspot_21.0.5_11.zip";
    
    private readonly string _installDirectory;
    private readonly HttpClient _httpClient;

    public string DependencyName => "PlantUML";
    public string Description => "PlantUML diagram generator (includes portable Java runtime)";

    public PlantUmlInstaller(string? installDirectory = null, HttpClient? httpClient = null)
    {
        _installDirectory = installDirectory ?? GetDefaultInstallDirectory();
        _httpClient = httpClient ?? new HttpClient();
    }

    private static string GetDefaultInstallDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "FindNeedle", "Dependencies", "PlantUML");
    }

    public DependencyStatus GetStatus()
    {
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
        }

        return status;
    }

    public bool IsInstalled()
    {
        var jarPath = GetPlantUmlJarPath();
        var javaPath = GetJavaPath();

        return File.Exists(jarPath) && File.Exists(javaPath);
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
            return javaExe;

        // Check subdirectories (JRE extracts with version folder)
        if (Directory.Exists(javaDir))
        {
            foreach (var dir in Directory.GetDirectories(javaDir))
            {
                var nestedJava = Path.Combine(dir, "bin", "java.exe");
                if (File.Exists(nestedJava))
                    return nestedJava;
            }
        }

        return null;
    }

    private string? GetVersion()
    {
        try
        {
            var javaPath = GetJavaPath();
            var jarPath = GetPlantUmlJarPath();
            
            if (javaPath == null || jarPath == null)
                return null;

            var psi = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = $"-jar \"{jarPath}\" -version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            var output = process?.StandardOutput.ReadToEnd();
            process?.WaitForExit(5000);
            
            return output?.Trim().Split('\n').FirstOrDefault();
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

            // Install Java JRE if needed
            if (GetJavaPath() == null)
            {
                progress?.Report(new InstallProgress { Status = "Downloading Java runtime...", PercentComplete = 0 });
                await DownloadAndExtractJavaAsync(progress, cancellationToken);
            }

            // Install PlantUML JAR
            progress?.Report(new InstallProgress { Status = "Downloading PlantUML...", PercentComplete = 70 });
            await DownloadPlantUmlJarAsync(progress, cancellationToken);

            progress?.Report(new InstallProgress { Status = "Installation complete!", PercentComplete = 100 });

            return InstallResult.Succeeded(GetPlantUmlJarPath()!);
        }
        catch (Exception ex)
        {
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
            progress?.Report(new InstallProgress { Status = "Downloading Java runtime...", PercentComplete = 10 });
            
            using (var response = await _httpClient.GetAsync(JavaWindowsX64Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                
                await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream, cancellationToken);
            }

            // Extract
            progress?.Report(new InstallProgress { Status = "Extracting Java runtime...", PercentComplete = 50 });
            
            if (Directory.Exists(jreDir))
                Directory.Delete(jreDir, true);
            
            ZipFile.ExtractToDirectory(zipPath, jreDir, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
        }
    }

    private async Task DownloadPlantUmlJarAsync(
        IProgress<InstallProgress>? progress,
        CancellationToken cancellationToken)
    {
        var jarPath = Path.Combine(_installDirectory, PlantUmlJarName);

        progress?.Report(new InstallProgress { Status = "Downloading PlantUML jar...", PercentComplete = 80 });

        using var response = await _httpClient.GetAsync(PlantUmlJarUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var fileStream = new FileStream(jarPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fileStream, cancellationToken);
    }
}
