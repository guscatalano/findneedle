using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using findneedle.Utils;
using FindNeedlePluginLib.Interfaces;
using FindPluginCore.PluginSubsystem;

namespace findneedle.PluginSubsystem;
public class PluginManager
{
    //todo: Figure out something better
    public static readonly string FAKE_LOADER = "FakeLoadPlugin.exe";

    public static readonly string LOADER_CONFIG = "PluginConfig.json";


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
                    Process p = Process.Start(FAKE_LOADER, file);
                    
                    p.WaitForExit();
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

    private readonly string loadedConfig = "";
    public PluginConfig? config;
    public List<PluginDescription> loadedPlugins = new();
    public Dictionary<string, List<InMemoryPlugin>> pluginsLoadedByPath = new();
    public Dictionary<string, List<InMemoryPlugin>> pluginsLoadedByType = new();

    public PluginManager(string configFileToLoad = "")
    {
        if (string.IsNullOrEmpty(configFileToLoad))
        {
            configFileToLoad = LOADER_CONFIG;
        }
        if (File.Exists(configFileToLoad))
        {
            var json = File.ReadAllText(configFileToLoad);
           

            var options = new JsonSerializerOptions
            {
                IncludeFields = true,

            };
            config = JsonSerializer.Deserialize<PluginConfig>(json, options);
            if (config == null)
            {
                throw new Exception("Failed to deserialize");
            }
        } 
        else
        {
            config = new PluginConfig();
        }
        loadedConfig = configFileToLoad;

    }

    public void SaveToFile(string configFileToSave = "")
    {
        if (string.IsNullOrEmpty(configFileToSave))
        {
            configFileToSave = loadedConfig;
        }
        string output = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            IncludeFields = true,
        });
        File.WriteAllText(configFileToSave, output);
    }

    public List<InMemoryPlugin> GetAllPluginsOfAType(string interfaceType)
    {
        return pluginsLoadedByType[interfaceType];
    }

    public void LoadAllPlugins(bool loadIntoAssembly = true)
    {
        if (config != null)
        {
            foreach (var entry in config.entries)
            {

                if (!File.Exists(entry.path))
                {
                    throw new Exception("Can't find plugin");
                }
                entry.path = Path.GetFullPath(entry.path);

                List<PluginDescription>? pluginLoad = LoadOnePlugin(entry.path);
                    
                //This is a valid plugin
                if(pluginLoad != null && loadIntoAssembly)
                {
                    InMemoryPlugin loadedPlugin = new(entry.path, pluginLoad);
                    foreach (var pluginDescription in pluginLoad)
                    {
                        if (!pluginsLoadedByPath.ContainsKey(entry.path))
                        {
                            pluginsLoadedByPath.Add(entry.path, new List<InMemoryPlugin>());
                        }
                        pluginsLoadedByPath[entry.path].Add(loadedPlugin);
                        foreach (var pluginImplementation in pluginDescription.ImplementedInterfaces) {
                            if (!pluginsLoadedByType.ContainsKey(pluginImplementation))
                            {
                                pluginsLoadedByType.Add(pluginImplementation, new List<InMemoryPlugin>());
                            }

                            pluginsLoadedByType[pluginImplementation].Add(loadedPlugin);
                            
                        }
                    }
                }
              
            }
        }
    }

    public string GetFakeLoadPluginPath()
    {
        config ??= new PluginConfig();
        if (String.IsNullOrEmpty(config.PathToFakeLoadPlugin))
        {
            config.PathToFakeLoadPlugin = FAKE_LOADER;
        }
        if(!File.Exists(config.PathToFakeLoadPlugin))
        {
            throw new Exception("can't find fake loader for plugins");
        }
        return config.PathToFakeLoadPlugin;
    }


    public List<PluginDescription>? LoadOnePlugin(string path)
    {

        var descriptorFile = path + ".json";
        Process p = Process.Start(GetFakeLoadPluginPath(), path);

        p.WaitForExit();
        if (File.Exists(descriptorFile))
        {
            return IPluginDescription.ReadDescriptionFile(descriptorFile);
        }
        else
        {
            Console.WriteLine("Plugin loader failed to load " + path);
        }

        return null;

    }

}
