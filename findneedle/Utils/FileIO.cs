using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace findneedle.Utils;
public class FileIO
{

    public delegate void GetAllFilesErrorCallback(string path);

    public static IEnumerable<string> GetAllFiles(string path, GetAllFilesErrorCallback? errorHandler = null)
    {
        Queue<string> queue = new Queue<string>();
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
