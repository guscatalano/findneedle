using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace FindNeedleToolInstallers;

public class PlantUmlInstaller : IDependencyInstaller, IPlantUmlInstaller
{
    private const string PlantUmlJarUrl = "https://github.com/plantuml/plantuml/releases/download/v1.2025.2/plantuml-mit-1.2025.2.jar";
    private const string PlantUmlJarName = "plantuml-mit-1.2025.2.jar";

    private readonly string _installDirectory;
    private readonly HttpClient _httpClient;

    public string DependencyName => "PlantUML";
    public string Description => "PlantUML diagram generator (includes portable Java runtime)";

    public PlantUmlInstaller(string? installDirectory = null, HttpClient? httpClient = null)
    {
        _installDirectory = installDirectory ?? GetDefaultInstallDir("PlantUML");
        _httpClient = httpClient ?? new HttpClient();
    }

    private static string GetDefaultInstallDir(string name)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "FindNeedle", "Dependencies", name);
    }

    public DependencyStatus GetStatus()
    {
        return new DependencyStatus
        {
            Name = DependencyName,
            Description = Description,
            IsInstalled = IsInstalled(),
            InstalledPath = GetPlantUmlJarPath(),
            InstallInstructions = "Download PlantUML JAR file and Java runtime"
        };
    }

    public bool IsInstalled()
    {
        var jar = GetPlantUmlJarPath();
        return jar != null && File.Exists(jar);
    }

    public string? GetPlantUmlJarPath()
    {
        var path = Path.Combine(_installDirectory, PlantUmlJarName);
        return File.Exists(path) ? path : null;
    }

    public string? GetJavaPath()
    {
        var javaPath = Path.Combine(_installDirectory, "jre", "bin", "java.exe");
        return File.Exists(javaPath) ? javaPath : null;
    }

    public async Task<InstallResult> InstallAsync(IProgress<InstallProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_installDirectory);
            var jarPath = Path.Combine(_installDirectory, PlantUmlJarName);
            using var resp = await _httpClient.GetAsync(PlantUmlJarUrl, cancellationToken);
            resp.EnsureSuccessStatusCode();
            await using var fs = new FileStream(jarPath, FileMode.Create, FileAccess.Write);
            await resp.Content.CopyToAsync(fs, cancellationToken);
            return InstallResult.Succeeded(jarPath);
        }
        catch (Exception ex)
        {
            return InstallResult.Failed(ex.Message);
        }
    }
}
