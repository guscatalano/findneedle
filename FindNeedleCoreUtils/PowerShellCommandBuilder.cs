using System;

namespace FindNeedleCoreUtils;

/// <summary>
/// Helper utilities for building and escaping commands for PowerShell execution.
/// This is extracted to make the logic testable without needing to actually run commands.
/// </summary>
public static class PowerShellCommandBuilder
{
    /// <summary>
    /// Escapes a string for use in PowerShell single-quoted strings by doubling single quotes.
    /// </summary>
    public static string EscapeForPowerShell(string input)
    {
        return input.Replace("'", "''");
    }

    /// <summary>
    /// Builds the PATH environment variable setup command.
    /// </summary>
    public static string BuildPathSetupCommand(string[] pathAdditions)
    {
        if (pathAdditions == null || pathAdditions.Length == 0)
            return "";

        var escapedPaths = string.Join(";", pathAdditions.Select(EscapeForPowerShell));
        return $"$env:PATH = ''{escapedPaths};'' + $env:PATH; ";
    }

    /// <summary>
    /// Builds the inner PowerShell command that runs the actual executable.
    /// </summary>
    public static string BuildInnerCommand(
        string workingDirectory,
        string executablePath,
        string arguments,
        string outputFile,
        string sentinelFile,
        string[] pathAdditions)
    {
        var pathSetup = BuildPathSetupCommand(pathAdditions);
        var escapedWorkingDir = EscapeForPowerShell(workingDirectory);
        var escapedExePath = EscapeForPowerShell(executablePath);
        var escapedArgs = EscapeForPowerShell(arguments);
        var escapedOutputFile = EscapeForPowerShell(outputFile);
        var escapedSentinelFile = EscapeForPowerShell(sentinelFile);

        return $"{pathSetup}Set-Location ''{escapedWorkingDir}''; " +
               $"& ''{escapedExePath}'' {escapedArgs} *> ''{escapedOutputFile}''; " +
               $"$LASTEXITCODE | Out-File -FilePath ''{escapedSentinelFile}'' -Encoding ASCII";
    }

    /// <summary>
    /// Builds the full Invoke-CommandInDesktopPackage PowerShell command.
    /// </summary>
    public static string BuildPackageContextCommand(
        string packageFamilyName,
        string innerCommand)
    {
        var escapedPackageFamilyName = EscapeForPowerShell(packageFamilyName);
        var innerArgs = $"-WindowStyle Hidden -NoProfile -Command \"{innerCommand}\"";
        
        return $"Invoke-CommandInDesktopPackage -PackageFamilyName '{escapedPackageFamilyName}' " +
               $"-AppId 'App' -Command 'powershell.exe' -Args '{innerArgs}' -PreventBreakaway";
    }
}
