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
using FindPluginCore.GlobalConfiguration;
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

    public string CallFakeLoadPlugin(string plugin)
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly == null)
        {
            throw new Exception("Entry assembly is null");
        }

        ProcessStartInfo ps = new()
        {
            FileName = GetFakeLoadPluginPath(),
            Arguments = plugin,
            WorkingDirectory = Path.GetDirectoryName(entryAssembly.Location) ?? throw new Exception("Failed to get directory of entry assembly"),
            UseShellExecute = false,
            RedirectStandardError = GlobalSettings.Debug,
            RedirectStandardOutput = GlobalSettings.Debug
        };
        var eOut = "Output is disabled";
        Process p = new Process();
        p.StartInfo = ps;
        if (GlobalSettings.Debug)
        {
            eOut = "";
            p.ErrorDataReceived += new DataReceivedEventHandler((sender, e) =>
            {
                eOut += e.Data;
            });
            p.OutputDataReceived += new DataReceivedEventHandler((sender, e) =>
            {
                eOut += e.Data;
            });
            p.EnableRaisingEvents = true;
        }
        p.Start();
        if (GlobalSettings.Debug)
        {
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }

        p.WaitForExit();
        return eOut;
    }

    private readonly string loadedConfig = "";
    public PluginConfig? config;
    public List<InMemoryPluginModule> loadedPluginsModules = new();


    public void PrintToConsole()
    {
        Console.WriteLine("Loaded ("+ loadedPluginsModules.Count+") plugin modules.");
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

  

    public List<T> GetAllPluginsInstancesOfAType<T>()
    {
        List<T> ret = new();
        foreach (var pluginModule in loadedPluginsModules)
        {
            foreach (var plugin in pluginModule.description)
            {
                //Skip it if it doesnt implement the interface we need
                if(plugin.ImplementedInterfaces.FirstOrDefault(x => x.Equals(typeof(T).FullName)) == null)
                {
                    continue;
                }
                var inMemoryPluginObject = pluginModule.GetObjectForType<T>(plugin);
                if (inMemoryPluginObject is InMemoryPluginObject<T> instance && instance != null)
                {
                    var createdInstance = instance.CreateInstance();
                    if (createdInstance != null)
                    {
                        ret.Add(createdInstance);
                    }
                }
            }
        }
        return ret;
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
                    throw new Exception("Can't find plugin module for " + pluginModuleDescriptor.path);
                }
                

                InMemoryPluginModule loadedPluginModule = new(pluginModuleDescriptor.path, this, loadIntoAssembly);
                loadedPluginsModules.Add(loadedPluginModule);
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


   

}
