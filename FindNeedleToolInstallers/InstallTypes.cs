namespace FindNeedleToolInstallers;

public record InstallResult(bool Success, string? Path, string? Message)
{
    public static InstallResult Succeeded(string path) => new(true, path, null);
    public static InstallResult Failed(string message) => new(false, null, message);
}

public class InstallProgress
{
    public string Status { get; set; } = string.Empty;
    public int PercentComplete { get; set; }
    public bool IsIndeterminate { get; set; }
}

public class DependencyStatus
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsInstalled { get; set; }
    public string? InstalledPath { get; set; }
    public string? InstalledVersion { get; set; }
    public string? InstallInstructions { get; set; }
}
