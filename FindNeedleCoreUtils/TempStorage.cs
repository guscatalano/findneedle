using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedleCoreUtils;
public class TempStorage : IDisposable
{

    private static readonly TempStorage gTemp = new();

    public static TempStorage GetSingleton()
    {
        return gTemp;
    }

    /**
     * Gets the default main temp path from the singleton
     */
    public static string GetMainTempPath()
    {
        var tempPath = gTemp.GetExistingMainTempPath();
        if(tempPath == null || tempPath.Count() == 0)
        {
            throw new Exception("Temp path was cleared unexpectedly, this is initialized in the constructor");
        }

        return tempPath;

    }

    public static void DeleteSomeTempPath(string randomDir)
    {
        while (Directory.Exists(randomDir))
        {
            Directory.Delete(randomDir, true);
            Thread.Sleep(1000);
        }
    }

    /**
    * Generates a new temp path without replacing the main temp one
    * Caller is responsible for cleanup
    */
    public static string GetNewTempPath(string hint)
    {
        return gTemp.GetNewTempPathWithHint(hint);
    }


    public TempStorage()
    {

        tempPath = GenerateNewPath(Path.GetTempPath()); 

        var dir = Directory.CreateDirectory(tempPath);
        if (!dir.Exists)
        {
            throw new Exception("Failed to create temp dir");
        }
        
    }

    ~TempStorage()
    {
        Dispose();
    }

    /**
   * Generates a new temp path without replacing the main temp one
   * Caller is responsible for cleanup
   */
    public string GetNewTempPathWithHint(string hint)
    {
        var newPath = gTemp.GenerateNewPath(GetMainTempPath(), hint);
        var dir = Directory.CreateDirectory(newPath);
        if (!dir.Exists)
        {
            throw new Exception("Failed to create temp dir");
        }
        return newPath;
    }


    /**
     * Generates a random folder name based on the hint
     */
    public string GenerateRandomFolderName(string hint)
    {
        var n = new Random();
        return hint + "_" + DateTime.Now.Day + DateTime.Now.Month + DateTime.Now.Hour + DateTime.Now.Minute + DateTime.Now.Second + "_" + n.Next(10000);
    }

    public string tempPath
    {
        get; set;
    }
    
    //Avoid reusing
    public static List<string> generatedPaths = new();

    /*
     * Generates a new path with the root as the base and the hint as a way to indicate what the folder is for
     */
    public string GenerateNewPath(string root, string hint = "FindNeedleTemp")
    {
        generatedPaths ??= [];
        var max = 10000;
        string ntempPath;
        do
        {
            max--;
            ntempPath = Path.Combine(root, GenerateRandomFolderName(hint));
            if (max == 0)
            {
                throw new Exception("Could not find a unique temp path");
            }
        } while (Path.Exists(ntempPath) || generatedPaths.Contains(ntempPath));
        generatedPaths.Add(ntempPath);
        return ntempPath;
    }

    /**
     * Gets the existing main temp path
     */
    public string GetExistingMainTempPath()
    {
        return tempPath;
    }

    

    /**
     * Cleans up the temp path
     */
    public void Dispose()
    {
        TempStorage.DeleteSomeTempPath(tempPath);
    }


}
