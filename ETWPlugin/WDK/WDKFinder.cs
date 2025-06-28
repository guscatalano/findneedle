using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedleCoreUtils;
using Microsoft.Win32;

namespace findneedle.WDK;
public class WDKFinder
{
    public static bool TEST_MODE = false; //used for unit testing
    public static bool TEST_MODE_SUCCESS = false; //used for unit testing
    public static bool TEST_MODE_PASS_FMT_PATH = false;
    public static string TEST_MODE_FMT_PATH = "";
    public const string TEST_MODE_FAKE_SIMPLE_PATH = "C:\\Program Files (x86)\\Windows Kits\\10\\bin\\10.0.22621.0\\";
    public const string TET_MODE_FAKE_ROOT_PATH = "C:\\Program Files (x86)\\Windows Kits\\10\\";

    public const string TRACE_FMT_NAME = "tracefmt.exe";
    public const string TRACE_FMT_ARCH = "x64";
    public const string NOT_FOUND_STRING = "NOTFOUND";
    public const string REG_PATH_ROOT = @"SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots";
    public const string REG_PATH_ROOT_KIT_NAME = @"KitsRoot10";
    public const string REG_PATH_ROOT_BIN_NAME = @"WdkBinRootVersioned";

    public static void ResetTestFlags()
    {
        TEST_MODE = false;
        TEST_MODE_SUCCESS = false;
        TEST_MODE_PASS_FMT_PATH = false;
    }


    private static string GetPathOfWDKRoot()
    {
        if (TEST_MODE)
        {
            if (TEST_MODE_SUCCESS)
            {
                return TET_MODE_FAKE_ROOT_PATH;
            }
            else
            {
                return NOT_FOUND_STRING;
            }
        }
        try
        {
            var key = Registry.LocalMachine.OpenSubKey(REG_PATH_ROOT);
            if (key == null)
            {
                return NOT_FOUND_STRING;
            }
            var ret = key.GetValue("KitsRoot10");
            if (ret == null)
            {
                return NOT_FOUND_STRING;
            }
            return ((string)ret).ToString();
        }
        catch
        {
            return NOT_FOUND_STRING;
        }
    }

    private static string SearchWDKForFile(string file, string arch)
    {
        var searchFailed = false;
        var ret = FileIO.GetAllFiles(GetPathOfWDKRoot(), (path) => { searchFailed = true; }).ToList();
        var foundVersions = new List<string>();
        if (!searchFailed)
        {
            try
            {
                foreach (var candidate in ret)
                {
                    if (candidate.Contains(file) && candidate.Contains(arch))
                    {
                        foundVersions.Add(candidate);
                    }
                }
            }
            catch (Exception)
            {
                //Do nothing, we'll handle it below as needed
            }
        }

        if (TEST_MODE)
        {
            if (TEST_MODE_SUCCESS)
            {
                return Path.Combine(TEST_MODE_FAKE_SIMPLE_PATH, TRACE_FMT_ARCH, TRACE_FMT_NAME);
            }
            else
            {
                return NOT_FOUND_STRING;
            }
        }

        if (foundVersions.Count > 0)
        {
            foundVersions.Sort(); //Get latest version
            return foundVersions[foundVersions.Count - 1];
        }
        return NOT_FOUND_STRING;
    }

    private static string GetPathOfWDKEasy()
    {
        if (TEST_MODE)
        {
            if (TEST_MODE_SUCCESS)
            {
                return TEST_MODE_FAKE_SIMPLE_PATH;
            } else
            {
                return NOT_FOUND_STRING;
            }
        }

        //HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows Kits\Installed Roots
        //WdkBinRootVersioned C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\
        try
        {
            var key = Registry.LocalMachine.OpenSubKey(REG_PATH_ROOT);
            if (key == null)
            {
                return NOT_FOUND_STRING;
            }
            var ret = key.GetValue(REG_PATH_ROOT_BIN_NAME);
            if (ret == null)
            {
                return NOT_FOUND_STRING;
            }
            return ((string)ret).ToString();
        }
        catch
        {
            return NOT_FOUND_STRING;
        }
    }

    public static string GetTraceFmtPath()
    {
        var wdk = GetPathOfWDKEasy();
        var potentialPath = Path.Combine(wdk, TRACE_FMT_ARCH, TRACE_FMT_NAME);

        //We are providing a path in testing manually
        if(TEST_MODE && TEST_MODE_PASS_FMT_PATH)
        {
            potentialPath = Path.GetFullPath(TEST_MODE_FMT_PATH);
            if (File.Exists(potentialPath))
            {
                return TEST_MODE_FMT_PATH;
            }
            else
            {
                throw new Exception("TEST_MODE_FMT_PATH does not exist. " + potentialPath);
            }
        }

        if (File.Exists(potentialPath) && !TEST_MODE)
        {
            return potentialPath;
        }
        else
        {
            return SearchWDKForFile(TRACE_FMT_NAME, TRACE_FMT_ARCH);
        }
    }
}
