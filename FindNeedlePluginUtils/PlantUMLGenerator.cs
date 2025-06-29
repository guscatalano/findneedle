using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using FindNeedlePluginLib;

namespace FindNeedlePluginUtils;

public class PlantUMLGenerator
{
    private static string? _cachedPlantUMLPath = null;

    public string GenerateUML(string umlinput)
    {
        if(IsSupported() == false)
        {
            throw new Exception("PlantUML is not supported");
        }

        if (!Path.Exists(umlinput))
        {
            throw new Exception("invalid input");
        }
        ProcessStartInfo processStartInfo = new ProcessStartInfo
        {
            FileName = "java",
            Arguments = $"-jar \"{GetPlantUMLPath()}\" " + umlinput,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        Process process = new Process
        {
            StartInfo = processStartInfo
        };
        process.Start();
        process.WaitForExit();
        var expectedOutput = umlinput.Replace(".pu", ".png");
        if (Path.Exists(expectedOutput))
        {
            return expectedOutput;
        } else
        {
            throw new Exception("failed to get uml output");
        }
    }

    public bool IsSupported()
    {
        if (IsJavaRuntimeInstalled())
        {
            if (Path.Exists(GetPlantUMLPath())){ 
                return true;
            }
        }
        return false;
    }

    public string GetPlantUMLPath()
    {
        if (_cachedPlantUMLPath != null)
            return _cachedPlantUMLPath;

        // Use PluginSubsystemAccessorProvider if available
        var accessor = PluginSubsystemAccessorProvider.Accessor;
        if (accessor != null && !string.IsNullOrWhiteSpace(accessor.PlantUMLPath))
        {
            _cachedPlantUMLPath = accessor.PlantUMLPath;
            return _cachedPlantUMLPath;
        }

        // Fallback to default
        _cachedPlantUMLPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "plantuml-mit-1.2025.2.jar");
        return _cachedPlantUMLPath;
    }

    public bool IsJavaRuntimeInstalled()
    {
        try
        {
            var rk = Registry.LocalMachine;
            var subKey = rk.OpenSubKey("SOFTWARE\\WOW6432Node\\JavaSoft\\Java Runtime Environment");

            if (subKey == null)
            {
                return false;
            }

            var currentVersion = subKey.GetValue("CurrentVersion")?.ToString();
            if (string.IsNullOrEmpty(currentVersion))
            {
                return false;
            }
            else
            {
                return true;
            }
        }
        catch
        {
            return false;
        }
    }
}
