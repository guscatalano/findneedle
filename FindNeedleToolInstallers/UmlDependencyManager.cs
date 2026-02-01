using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FindNeedleToolInstallers;

/// <summary>
/// Manages UML generator dependencies and their installation.
/// Provides a high-level API for installing and accessing PlantUML and Mermaid CLI tools.
/// </summary>
public class UmlDependencyManager
{
    private readonly PlantUmlInstaller _plantUmlInstaller;
    private readonly MermaidInstaller _mermaidInstaller;

    public UmlDependencyManager(string? baseInstallDirectory = null)
    {
        var baseDir = baseInstallDirectory ?? GetDefaultBaseDirectory();
        
        _plantUmlInstaller = new PlantUmlInstaller(Path.Combine(baseDir, "PlantUML"));
        _mermaidInstaller = new MermaidInstaller(Path.Combine(baseDir, "Mermaid"));
    }

    private static string GetDefaultBaseDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "FindNeedle", "Dependencies");
    }

    /// <summary>
    /// Gets the PlantUML installer.
    /// </summary>
    public PlantUmlInstaller PlantUml => _plantUmlInstaller;

    /// <summary>
    /// Gets the Mermaid installer.
    /// </summary>
    public MermaidInstaller Mermaid => _mermaidInstaller;

    /// <summary>
    /// Gets all available installers.
    /// </summary>
    public IEnumerable<IDependencyInstaller> AllInstallers => new IDependencyInstaller[]
    {
        _plantUmlInstaller,
        _mermaidInstaller
    };

    /// <summary>
    /// Gets the status of all dependencies.
    /// </summary>
    public IEnumerable<DependencyStatus> GetAllStatuses()
    {
        return AllInstallers.Select(i => i.GetStatus());
    }

    /// <summary>
    /// Checks if all dependencies for image generation are installed.
    /// </summary>
    public bool AreAllImageDependenciesInstalled()
    {
        return _plantUmlInstaller.IsInstalled() && _mermaidInstaller.IsInstalled();
    }

    /// <summary>
    /// Installs all missing dependencies.
    /// </summary>
    public async Task<Dictionary<string, InstallResult>> InstallAllMissingAsync(
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, InstallResult>();
        var installers = AllInstallers.Where(i => !i.IsInstalled()).ToList();
        var totalInstallers = installers.Count;
        var current = 0;

        foreach (var installer in installers)
        {
            var installerProgress = new Progress<InstallProgress>(p =>
            {
                var overallPercent = (current * 100 + p.PercentComplete) / totalInstallers;
                progress?.Report(new InstallProgress
                {
                    Status = $"[{installer.DependencyName}] {p.Status}",
                    PercentComplete = overallPercent,
                    IsIndeterminate = p.IsIndeterminate
                });
            });

            var result = await installer.InstallAsync(installerProgress, cancellationToken);
            results[installer.DependencyName] = result;
            current++;
        }

        return results;
    }

    /// <summary>
    /// Gets installation summary for display.
    /// </summary>
    public string GetInstallationSummary()
    {
        var lines = new List<string>
        {
            "UML Generator Dependencies",
            "=========================="
        };

        foreach (var status in GetAllStatuses())
        {
            var installed = status.IsInstalled ? "? Installed" : "? Not Installed";
            lines.Add($"\n{status.Name}: {installed}");
            
            if (status.IsInstalled)
            {
                if (!string.IsNullOrEmpty(status.InstalledVersion))
                    lines.Add($"  Version: {status.InstalledVersion}");
                if (!string.IsNullOrEmpty(status.InstalledPath))
                    lines.Add($"  Path: {status.InstalledPath}");
            }
            else
            {
                lines.Add($"  {status.InstallInstructions}");
            }
        }

        return string.Join("\n", lines);
    }
}
