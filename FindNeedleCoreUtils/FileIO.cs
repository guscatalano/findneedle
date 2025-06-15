using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindNeedleCoreUtils;
public class FileIO
{

    public static string FindFullPathToFile(string fileName)
    {
        var useDefault = Path.GetFullPath(fileName);
        var useRelative = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

        //Calling it AI path, cause AI suggested it
        var useAIPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FindNeedle", fileName);
        if(File.Exists(fileName))
        {
            return fileName; //If the file is in the current directory, just return it
        }
        else if (File.Exists(useDefault))
        {
            return useDefault;
        }
        else if (File.Exists(useRelative))
        {
            return useRelative;
        }
        else if (File.Exists(useAIPath))
        {
            return useAIPath;
        }
        else
        {
            //If we get here, we have no idea where the file is
            throw new FileNotFoundException($"File {fileName} not found in any of the expected locations.");
        }
    }

    public delegate void GetAllFilesErrorCallback(string path);

    public static IEnumerable<string> GetAllFiles(string path, GetAllFilesErrorCallback? errorHandler = null)
    {
        var queue = new Queue<string>();
        queue.Enqueue(path);
        while (queue.Count > 0)
        {
            path = queue.Dequeue();
            try
            {
                foreach (var subDir in Directory.GetDirectories(path))
                {
                    queue.Enqueue(subDir);
                }
            }
            catch (Exception)
            {
                if (errorHandler != null)
                {
                    errorHandler(path);
                }
            }
            var files = new string[1];
            try
            {
                files = Directory.GetFiles(path);
            }
            catch (Exception)
            {
                if (errorHandler != null)
                {
                    errorHandler(path);
                }
            }
            if (files != null)
            {
                for (var i = 0; i < files.Length; i++)
                {
                    yield return files[i];
                }
            }
        }
    }
}
