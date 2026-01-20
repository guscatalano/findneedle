namespace FindNeedlePluginUtils.DependencyInstaller;

/// <summary>
/// Progress information for dependency installation.
/// </summary>
public class InstallProgress
{
    public string Status { get; set; } = string.Empty;
    public int PercentComplete { get; set; }
    public bool IsIndeterminate { get; set; }
}

/// <summary>
/// Result of a dependency installation attempt.
/// </summary>
public class InstallResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? InstalledPath { get; set; }

    public static InstallResult Succeeded(string installedPath) => new()
    {
        Success = true,
        InstalledPath = installedPath
    };

    public static InstallResult Failed(string error) => new()
    {
        Success = false,
        ErrorMessage = error
    };
}

/// <summary>
/// Information about a dependency's current state.
/// </summary>
public class DependencyStatus
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsInstalled { get; set; }
    public string? InstalledVersion { get; set; }
    public string? InstalledPath { get; set; }
    public string? InstallInstructions { get; set; }
}

/// <summary>
/// Interface for dependency installers.
/// </summary>
public interface IDependencyInstaller
{
    /// <summary>
    /// Name of the dependency (e.g., "Java Runtime", "Node.js").
    /// </summary>
    string DependencyName { get; }

    /// <summary>
    /// Description of what this dependency is used for.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Gets the current status of the dependency.
    /// </summary>
    DependencyStatus GetStatus();

    /// <summary>
    /// Checks if the dependency is installed and functional.
    /// </summary>
    bool IsInstalled();

    /// <summary>
    /// Installs the dependency.
    /// </summary>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result of the installation.</returns>
    Task<InstallResult> InstallAsync(
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
