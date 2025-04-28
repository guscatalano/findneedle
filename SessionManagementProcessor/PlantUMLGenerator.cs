using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace SessionManagementProcessor;
public class PlantUMLGenerator
{

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
        //hack
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "plantuml-mit-1.2025.2.jar");
        return path;
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
