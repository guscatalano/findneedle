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
using FindNeedleCoreUtils;
using FindNeedlePluginLib.Interfaces;
using FindPluginCore.PluginSubsystem;

namespace findneedle.PluginSubsystem;
public class PluginManager
{
    private static PluginManager? gPluginManager = null;
    public static PluginManager GetSingleton()
    {
        gPluginManager ??= new PluginManager();
        return gPluginManager;
    }


    //todo: Figure out something better
    public static readonly string FAKE_LOADER = "FakeLoadPlugin.exe";

    public static readonly string LOADER_CONFIG = "PluginConfig.json";

    public static void CallFakeLoadPlugin(string loaderPath, string plugin)
    {
        ProcessStartInfo ps = new()
        {
            FileName = loaderPath,
            Arguments = plugin

        };
        var p = Process.Start(ps);

        if (p == null)
        {
            throw new Exception("Failed to start Plugin loader process");
        }

        p.WaitForExit();
    }


    public Dictionary<string, List<PluginDescription>> DiscoverPlugins()
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

                    //TODO read the fake loader path correctly
                    CallFakeLoadPlugin(GetFakeLoadPluginPath(), file);

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
    public Dictionary<string, List<InMemoryPluginObject<object>>> pluginsObjectLoadedByType = new();

    public void PrintToConsole()
    {
        Console.WriteLine("Loaded ("+ pluginsLoadedByPath.Count+") plugins: ");
        foreach (var entry in pluginsLoadedByPath)
        {
            Console.WriteLine(entry.Key);
            foreach (var plugin in entry.Value)
            {
                Console.WriteLine("  " + plugin.ToString());
            }
        }
        Console.WriteLine("FakeLoaderPath: " + config?.PathToFakeLoadPlugin);    
    }


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
            if (!string.IsNullOrEmpty(configFileToLoad))
            {
                //throw new Exception("Config file was specified and it doesnt exist");
            }
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
        var output = JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            IncludeFields = true,
        });
        File.WriteAllText(configFileToSave, output);
    }

    public List<InMemoryPlugin> GetAllPluginsOfAType(string interfaceType)
    {

        if (pluginsLoadedByType.ContainsKey(interfaceType))
        {
            return pluginsLoadedByType[interfaceType];
        }
        else
        {
            return new List<InMemoryPlugin>();
        }
    }

    public List<InMemoryPluginObject<object>> GetAllPluginObjectsOfAType(string interfaceType)
    {
        if (pluginsObjectLoadedByType.ContainsKey(interfaceType))
        {
            return pluginsObjectLoadedByType[interfaceType];
        } 
        else
        {
            return new List<InMemoryPluginObject<object>>();
        }
    }

    public void LoadAllPlugins(bool loadIntoAssembly = true)
    {
        if (config != null)
        {
            foreach (var entry in config.entries)
            {
                entry.path = Path.GetFullPath(entry.path);
                if (!File.Exists(entry.path))
                {
                    throw new Exception("Can't find plugin");
                }
                

                List<PluginDescription>? pluginLoad = LoadOnePlugin(entry.path);

                //This is a valid plugin
                if (pluginLoad != null && loadIntoAssembly)
                {
                    InMemoryPlugin loadedPlugin = new(entry.path, pluginLoad);
                    foreach (var pluginDescription in pluginLoad)
                    {
                        if (!pluginsLoadedByPath.ContainsKey(entry.path))
                        {
                            pluginsLoadedByPath.Add(entry.path, new List<InMemoryPlugin>());
                        }
                        pluginsLoadedByPath[entry.path].Add(loadedPlugin);
                        foreach (var pluginImplementationShort in pluginDescription.ImplementedInterfacesShort)
                        {
                            InMemoryPluginObject<object> obj = loadedPlugin.GetObjectForType(pluginDescription);
                            if (!pluginsLoadedByType.ContainsKey(pluginImplementationShort))
                            {
                                pluginsLoadedByType.Add(pluginImplementationShort, new List<InMemoryPlugin>());
                                pluginsObjectLoadedByType.Add(pluginImplementationShort, new List<InMemoryPluginObject<object>>());
                            }

                            pluginsLoadedByType[pluginImplementationShort].Add(loadedPlugin);
                            pluginsObjectLoadedByType[pluginImplementationShort].Add(obj);

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
        CallFakeLoadPlugin(GetFakeLoadPluginPath(), path);
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
