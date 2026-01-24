using System;
using System.Diagnostics;
using FindNeedlePluginLib;

namespace FindNeedlePluginUtils;

/// <summary>
/// Runs external commands in the correct context for packaged (MSIX) apps.
/// For packaged apps, uses Invoke-CommandInDesktopPackage to run commands outside the AppContainer.
/// For unpackaged apps, runs commands directly.
/// </summary>
public static class PackagedAppCommandRunner
{
    private static string? _cachedPackageFamilyName;
    private static bool _packageCheckDone;

    /// <summary>
    /// Gets the package family name if running as a packaged app, or null if unpackaged.
    /// </summary>
    public static string? PackageFamilyName
    {
        get
        {
            if (!_packageCheckDone)
            {
                try
                {
                    _cachedPackageFamilyName = global::Windows.ApplicationModel.Package.Current.Id.FamilyName;
                }
                catch
                {
                    _cachedPackageFamilyName = null;
                }
                _packageCheckDone = true;
            }
            return _cachedPackageFamilyName;
        }
    }

    /// <summary>
    /// Returns true if the app is running as a packaged MSIX app.
    /// </summary>
    public static bool IsPackagedApp => PackageFamilyName != null;

    /// <summary>
    /// Runs a command and returns the exit code.
    /// For packaged apps, uses Invoke-CommandInDesktopPackage with PowerShell to run hidden.
    /// </summary>
    /// <param name="executablePath">Full path to the executable to run.</param>
    /// <param name="arguments">Arguments to pass to the executable.</param>
    /// <param name="workingDirectory">Working directory for the process.</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default 60 seconds).</param>
    /// <returns>The process exit code.</returns>
    public static int RunCommand(string executablePath, string arguments, string workingDirectory, int timeoutMs = 60000)
    {
        if (IsPackagedApp)
        {
            return RunCommandViaPackageContext(executablePath, arguments, workingDirectory, timeoutMs);
        }
        else
        {
            return RunCommandDirectly(executablePath, arguments, workingDirectory, timeoutMs);
        }
    }

    /// <summary>
    /// Runs a command via Invoke-CommandInDesktopPackage for packaged apps.
    /// Uses PowerShell with -WindowStyle Hidden to prevent window flash.
    /// </summary>
    private static int RunCommandViaPackageContext(string executablePath, string arguments, string workingDirectory, int timeoutMs)
    {
        var packageFamilyName = PackageFamilyName!;
        
        // Escape single quotes for PowerShell by doubling them
        var escapedWorkingDir = workingDirectory.Replace("'", "''");
        var escapedExePath = executablePath.Replace("'", "''");
        var escapedArgs = arguments.Replace("'", "''");
        
        // Build the inner PowerShell command
        // The inner command runs in the package context with hidden window
        var innerCommand = $"Set-Location ''{escapedWorkingDir}''; & ''{escapedExePath}'' {escapedArgs}";
        var innerArgs = $"-WindowStyle Hidden -NoProfile -Command \"{innerCommand}\"";
        
        // Build the outer Invoke-CommandInDesktopPackage call
        var psCommand = $"Invoke-CommandInDesktopPackage -PackageFamilyName '{packageFamilyName}' -AppId 'App' -Command 'powershell.exe' -Args '{innerArgs}' -PreventBreakaway";
        
        Logger.Instance.Log($"[PackagedAppCommandRunner] Running via Invoke-CommandInDesktopPackage");
        Logger.Instance.Log($"[PackagedAppCommandRunner] Inner command: {innerCommand}");

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"{psCommand}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new Exception("Failed to start PowerShell process");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        var completed = process.WaitForExit(timeoutMs);

        if (!completed)
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException($"Command timed out after {timeoutMs}ms");
        }

        if (!string.IsNullOrWhiteSpace(stdout))
            Logger.Instance.Log($"[PackagedAppCommandRunner] stdout: {stdout}");
        if (!string.IsNullOrWhiteSpace(stderr))
            Logger.Instance.Log($"[PackagedAppCommandRunner] stderr: {stderr}");

        return process.ExitCode;
    }

    /// <summary>
    /// Runs a command directly (for unpackaged apps).
    /// </summary>
    private static int RunCommandDirectly(string executablePath, string arguments, string workingDirectory, int timeoutMs)
    {
        Logger.Instance.Log($"[PackagedAppCommandRunner] Running directly: {executablePath} {arguments}");

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            UseShellExecute = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new Exception($"Failed to start process: {executablePath}");
        }

        var completed = process.WaitForExit(timeoutMs);

        if (!completed)
        {
            try { process.Kill(); } catch { }
            throw new TimeoutException($"Command timed out after {timeoutMs}ms");
        }

        return process.ExitCode;
    }

    /// <summary>
    /// Runs a Java command with the specified JAR file and input.
    /// </summary>
    /// <param name="javaPath">Full path to java.exe</param>
    /// <param name="jarPath">Full path to the JAR file</param>
    /// <param name="inputPath">Input file path to pass to the JAR</param>
    /// <param name="workingDirectory">Working directory (typically the Java bin directory)</param>
    /// <param name="timeoutMs">Timeout in milliseconds</param>
    /// <returns>The process exit code</returns>
    public static int RunJavaJar(string javaPath, string jarPath, string inputPath, string workingDirectory, int timeoutMs = 60000)
    {
        var arguments = $"-jar \"{jarPath}\" \"{inputPath}\"";
        return RunCommand(javaPath, arguments, workingDirectory, timeoutMs);
    }
}
