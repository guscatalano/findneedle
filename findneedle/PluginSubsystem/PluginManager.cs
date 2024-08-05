using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using findneedle.Utils;
using FindNeedlePluginLib.Interfaces;

namespace findneedle.PluginSubsystem;
public class PluginManager
{
    //todo: Figure out something better
    public static readonly string FAKE_LOADER = "C:\\tools\\FakeLoadPlugin.exe";
    public static Dictionary<string, List<PluginDescription>> DiscoverPlugins()
    {
        Dictionary<string, List<PluginDescription>> ret = new();
        IEnumerable<string> files = FileIO.GetAllFiles(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        foreach (var file in files)
        {
            if (file.Contains("Plugin") && file.EndsWith(".dll"))
            {
                try
                {
                    var descriptorFile = file + ".json";
                    /*Process p = Process.Start(FAKE_LOADER, file);
                    
                    p.WaitForExit();*/
                    if (File.Exists(descriptorFile))
                    {
                        List<PluginDescription> plugins = IPluginDescription.ReadDescriptionFile(descriptorFile);   
                        ret.Add(file, plugins);
                    } else {
                        Console.WriteLine("Plugin loader failed to load " + file);
                    }
                }
                catch (Exception)
                {
                    //Dont care
                }
            }
           
        }
        return ret;
    }
}
