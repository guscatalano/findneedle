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

    public static void ResetSingleton()
    {
        gPluginManager = new PluginManager();
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
    public Dictionary<string, List<InMemoryPluginModule>> pluginsLoadedByPath = new();
    public Dictionary<string, List<InMemoryPluginModule>> pluginsLoadedByType = new();
    public Dictionary<string, List<InMemoryPluginObject<object>>> pluginsObjectLoadedByType = new();

    public void PrintToConsole()
    {
        Console.WriteLine("Loaded ("+ pluginsLoadedByPath.Count+") plugin files.");
        Console.WriteLine("Discovered (" + pluginsLoadedByType.Count + ") plugins.");
        foreach (var entry in pluginsLoadedByPath)
        {
            Console.WriteLine(entry.Key + " ("+ entry.Value.Count + ")");
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

    public List<InMemoryPluginModule> GetAllPluginsOfAType(string interfaceType)
    {

        if (pluginsLoadedByType.ContainsKey(interfaceType))
        {
            return pluginsLoadedByType[interfaceType];
        }
        else
        {
            return new List<InMemoryPluginModule>();
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
            foreach (var pluginModuleDescriptor in config.entries)
            {
                pluginModuleDescriptor.path = Path.GetFullPath(pluginModuleDescriptor.path);
                if (!File.Exists(pluginModuleDescriptor.path))
                {
                    throw new Exception("Can't find plugin module");
                }
                

                List<PluginDescription>? pluginDescriptors = LoadOnePlugin(pluginModuleDescriptor.path);

                //This is a valid plugin
                if (pluginDescriptors != null && loadIntoAssembly)
                {
                    InMemoryPluginModule loadedPluginModule = new(pluginModuleDescriptor.path, pluginDescriptors);
                    foreach (var pluginDescription in pluginDescriptors)
                    {
                        if (!pluginsLoadedByPath.ContainsKey(pluginModuleDescriptor.path))
                        {
                            pluginsLoadedByPath.Add(pluginModuleDescriptor.path, new List<InMemoryPluginModule>());
                        }
                        pluginsLoadedByPath[pluginModuleDescriptor.path].Add(loadedPluginModule);
                        foreach (var pluginImplementationShort in pluginDescription.ImplementedInterfacesShort)
                        {
                            InMemoryPluginObject<object> obj = loadedPluginModule.GetObjectForType(pluginDescription);
                            if (!pluginsLoadedByType.ContainsKey(pluginImplementationShort))
                            {
                                pluginsLoadedByType.Add(pluginImplementationShort, new List<InMemoryPluginModule>());
                                pluginsObjectLoadedByType.Add(pluginImplementationShort, new List<InMemoryPluginObject<object>>());
                            }

                            pluginsLoadedByType[pluginImplementationShort].Add(loadedPluginModule);
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
