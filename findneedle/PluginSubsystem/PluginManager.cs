using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using findneedle.Utils;

namespace findneedle.PluginSubsystem;
public class PluginManager
{

    public static void DiscoverPlugins()
    {
        IEnumerable<string> files = FileIO.GetAllFiles(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        foreach (string file in files)
        {
            if (file.StartsWith("FindNeedle") && file.Contains("Plugin") && file.EndsWith(".dll"))
            {
                try
                {
                    //var y = Assembly.ReflectionOnlyLoad()
                    // z = y.GetTypes();
                    //Assembly.l
                }
                catch (Exception)
                {

                }
            }
            Console.WriteLine(file);
        }
    }
}
