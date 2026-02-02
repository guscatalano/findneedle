using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace FindNeedleCoreUtils;

/// <summary>
/// Runs external commands in the correct context for packaged (MSIX) apps.
/// For packaged apps, uses Invoke-CommandInDesktopPackage to run commands outside the AppContainer.
/// For unpackaged apps, runs commands directly.
/// </summary>
public static class PackagedAppCommandRunner
{
    /// <summary>
    /// Gets the package family name if running as a packaged app, or null if unpackaged.
    /// </summary>
    public static string? PackageFamilyName => PackageContextProviderFactory.Current.PackageFamilyName;

    /// <summary>
    /// Returns true if the app is running as a packaged MSIX app.
    /// </summary>
    public static bool IsPackagedApp => PackageContextProviderFactory.Current.IsPackagedApp;

    /// <summary>
    /// Runs a command and returns the exit code.
    /// For packaged apps, uses Invoke-CommandInDesktopPackage with PowerShell to run hidden.
    /// </summary>
    /// <param name="executablePath">Full path to the executable to run.</param>
    /// <param name="arguments">Arguments to pass to the executable.</param>
    /// <param name="workingDirectory">Working directory for the process.</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default 60 seconds).</param>
    /// <param name="pathAdditions">Additional directories to add to PATH (e.g., Node.js directory).</param>
    /// <returns>The process exit code.</returns>
    public static int RunCommand(string executablePath, string arguments, string workingDirectory, int timeoutMs = 60000, string[]? pathAdditions = null)
    {
        if (IsPackagedApp)
        {
            return RunCommandViaPackageContext(executablePath, arguments, workingDirectory, timeoutMs, pathAdditions);
        }
        else
        {
            return RunCommandDirectly(executablePath, arguments, workingDirectory, timeoutMs);
        }
    }

    /// <summary>
    /// Runs a command via Invoke-CommandInDesktopPackage for packaged apps.
    /// Uses a sentinel file approach to wait for the command to actually complete,
    /// since Invoke-CommandInDesktopPackage returns immediately without waiting.
    /// </summary>
    private static int RunCommandViaPackageContext(string executablePath, string arguments, string workingDirectory, int timeoutMs, string[]? pathAdditions = null)
    {
        var packageFamilyName = PackageFamilyName!;
        
        // Create unique files to track completion and capture output
        var guid = Guid.NewGuid().ToString("N");
        var sentinelFile = Path.Combine(Path.GetTempPath(), $"PackagedAppCmd_{guid}.txt");
        var outputFile = Path.Combine(Path.GetTempPath(), $"PackagedAppCmd_{guid}_output.txt");
        
        try
        {
            // Escape single quotes for PowerShell by doubling them
            var escapedWorkingDir = workingDirectory.Replace("'", "''");
            var escapedExePath = executablePath.Replace("'", "''");
            var escapedArgs = arguments.Replace("'", "''");
            var escapedSentinelFile = sentinelFile.Replace("'", "''");
            var escapedOutputFile = outputFile.Replace("'", "''");
            
            // Build PATH setup command if needed
            var pathSetup = "";
            if (pathAdditions != null && pathAdditions.Length > 0)
            {
                var escapedPaths = string.Join(";", pathAdditions.Select(p => p.Replace("'", "''")));
                pathSetup = $"$env:PATH = ''{escapedPaths};'' + $env:PATH; ";
            }
            
            // Build the inner PowerShell command that runs the actual command and captures output
            // Redirect both stdout and stderr to the output file, then write exit code to sentinel file
            var innerCommand = $"{pathSetup}Set-Location ''{escapedWorkingDir}''; " +
                               $"& ''{escapedExePath}'' {escapedArgs} *> ''{escapedOutputFile}''; " +
                               $"$LASTEXITCODE | Out-File -FilePath ''{escapedSentinelFile}'' -Encoding ASCII";
            var innerArgs = $"-WindowStyle Hidden -NoProfile -Command \"{innerCommand}\"";
            
            // Build the outer Invoke-CommandInDesktopPackage call
            var psCommand = $"Invoke-CommandInDesktopPackage -PackageFamilyName '{packageFamilyName}' -AppId 'App' -Command 'powershell.exe' -Args '{innerArgs}' -PreventBreakaway";
            
            
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                throw new Exception("Failed to start PowerShell process");
            }

            // Wait for the Invoke-CommandInDesktopPackage call to complete (this is fast, it just spawns the process)
            process.WaitForExit(30000);

            // Now wait for the actual command to complete by polling for the sentinel file
            var startTime = DateTime.UtcNow;
            var pollInterval = 500; // ms
            
            while (!File.Exists(sentinelFile))
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                if (elapsed > timeoutMs)
                {
                    throw new TimeoutException($"Command timed out after {timeoutMs}ms");
                }
                
                Thread.Sleep(pollInterval);
            }

            // Give a small delay for files to be fully written
            Thread.Sleep(100);
            
            // Read the exit code from sentinel file
            var exitCodeText = File.ReadAllText(sentinelFile).Trim();
            if (int.TryParse(exitCodeText, out var exitCode))
            {
                return exitCode;
            }
            else
            {
                // If we can't parse, assume success if the file was created
                return 0;
            }
        }
        finally
        {
            // Clean up temp files
            try
            {
                if (File.Exists(sentinelFile))
                {
                    File.Delete(sentinelFile);
                }
                if (File.Exists(outputFile))
                {
                    File.Delete(outputFile);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Runs a command directly (for unpackaged apps).
    /// </summary>
    private static int RunCommandDirectly(string executablePath, string arguments, string workingDirectory, int timeoutMs)
    {
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

    /// <summary>
    /// Runs a command and returns both the exit code and captured output.
    /// Useful for commands like --version where you need the output.
    /// </summary>
    public static (int ExitCode, string Output) RunCommandWithOutput(string executablePath, string arguments, string workingDirectory, int timeoutMs = 60000, string[]? pathAdditions = null)
    {
        if (IsPackagedApp)
        {
            return RunCommandWithOutputViaPackageContext(executablePath, arguments, workingDirectory, timeoutMs, pathAdditions);
        }
        else
        {
            return RunCommandWithOutputDirectly(executablePath, arguments, workingDirectory, timeoutMs, pathAdditions);
        }
    }

    private static (int ExitCode, string Output) RunCommandWithOutputViaPackageContext(string executablePath, string arguments, string workingDirectory, int timeoutMs, string[]? pathAdditions = null)
    {
        var packageFamilyName = PackageFamilyName!;
        
        var guid = Guid.NewGuid().ToString("N");
        var sentinelFile = Path.Combine(Path.GetTempPath(), $"PackagedAppCmd_{guid}.txt");
        var outputFile = Path.Combine(Path.GetTempPath(), $"PackagedAppCmd_{guid}_output.txt");
        
        try
        {
            var escapedWorkingDir = workingDirectory.Replace("'", "''");
            var escapedExePath = executablePath.Replace("'", "''");
            var escapedArgs = arguments.Replace("'", "''");
            var escapedSentinelFile = sentinelFile.Replace("'", "''");
            var escapedOutputFile = outputFile.Replace("'", "''");
            
            var pathSetup = "";
            if (pathAdditions != null && pathAdditions.Length > 0)
            {
                var escapedPaths = string.Join(";", pathAdditions.Select(p => p.Replace("'", "''")));
                pathSetup = $"$env:PATH = ''{escapedPaths};'' + $env:PATH; ";
            }
            
            var innerCommand = $"{pathSetup}Set-Location ''{escapedWorkingDir}''; " +
                               $"& ''{escapedExePath}'' {escapedArgs} *> ''{escapedOutputFile}''; " +
                               $"$LASTEXITCODE | Out-File -FilePath ''{escapedSentinelFile}'' -Encoding ASCII";
            var innerArgs = $"-WindowStyle Hidden -NoProfile -Command \"{innerCommand}\"";
            
            var psCommand = $"Invoke-CommandInDesktopPackage -PackageFamilyName '{packageFamilyName}' -AppId 'App' -Command 'powershell.exe' -Args '{innerArgs}' -PreventBreakaway";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(30000);

            var startTime = DateTime.UtcNow;
            var pollInterval = 500;
            
            while (!File.Exists(sentinelFile))
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                if (elapsed > timeoutMs)
                {
                    return (-1, "Command timed out");
                }
                Thread.Sleep(pollInterval);
            }

            Thread.Sleep(100);
            
            var output = File.Exists(outputFile) ? File.ReadAllText(outputFile).Trim() : "";
            var exitCodeText = File.ReadAllText(sentinelFile).Trim();
            var exitCode = int.TryParse(exitCodeText, out var code) ? code : -1;
            
            return (exitCode, output);
        }
        finally
        {
            try
            {
                if (File.Exists(sentinelFile)) File.Delete(sentinelFile);
                if (File.Exists(outputFile)) File.Delete(outputFile);
            }
            catch { }
        }
    }

    private static (int ExitCode, string Output) RunCommandWithOutputDirectly(string executablePath, string arguments, string workingDirectory, int timeoutMs, string[]? pathAdditions = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };

        if (pathAdditions != null && pathAdditions.Length > 0)
        {
            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            psi.Environment["PATH"] = string.Join(";", pathAdditions) + ";" + currentPath;
        }

        using var process = Process.Start(psi);
        if (process == null)
        {
            return (-1, "Failed to start process");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        var completed = process.WaitForExit(timeoutMs);

        if (!completed)
        {
            try { process.Kill(); } catch { }
            return (-1, "Command timed out");
        }

        var output = !string.IsNullOrWhiteSpace(stdout) ? stdout.Trim() : stderr.Trim();
        return (process.ExitCode, output);
    }
}
