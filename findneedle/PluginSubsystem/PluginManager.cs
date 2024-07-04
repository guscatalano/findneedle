using System;
using System.Collections.Generic;
using System.Linq;
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
            Console.WriteLine(file);
        }
    }
}
