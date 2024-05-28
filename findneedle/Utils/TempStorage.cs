using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Utils;
public class TempStorage
{

    private static readonly TempStorage gTemp = new();

    public static TempStorage GetSingleton()
    {
        return gTemp;
    }

    public static string GetMainTempPath()
    {
        if(gTemp.tempPath == null || gTemp.tempPath.Count() == 0)
        {

        }
        return gTemp.tempPath;
    }

    public static string GetNewTempPath(string hint)
    {
        string newPath = gTemp.GenerateNewPath(GetMainTempPath(), hint);
        DirectoryInfo dir = Directory.CreateDirectory(newPath);
        if (!dir.Exists)
        {
            throw new Exception("Failed to create temp dir");
        }
        return newPath;
    }


    public string GenerateRandomFolderName(string hint)
    {
        Random n = new Random();
        return hint + "_" + DateTime.Now.Day + DateTime.Now.Month + DateTime.Now.Hour + DateTime.Now.Minute + DateTime.Now.Second + "_" + n.Next(10000);
    }

    public string tempPath
    {
        get; set;
    }

    public string GenerateNewPath(string root, string hint = "FindNeedleTemp")
    {
        int max = 10000;
        string ntempPath = "";
        do
        {
            max--;
            ntempPath = Path.Combine(root, GenerateRandomFolderName(hint));
            if (max == 0)
            {
                throw new Exception("Could not find a unique temp path");
            }
        } while (Path.Exists(ntempPath));
        return ntempPath;
    }

    public TempStorage()
    {

        tempPath = GenerateNewPath(Path.GetTempPath()); 

        DirectoryInfo dir = Directory.CreateDirectory(tempPath);
        if (!dir.Exists)
        {
            throw new Exception("Failed to create temp dir");
        }
        
    }

    ~TempStorage()
    {
        while (Directory.Exists(tempPath))
        {
            Thread.Sleep(1000);
            Directory.Delete(tempPath, true);
        }
    }

}
