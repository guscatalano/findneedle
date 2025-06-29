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
using FindNeedlePluginLib;
using FindPluginCore.GlobalConfiguration;
using FindPluginCore.PluginSubsystem;

namespace findneedle.PluginSubsystem;
public class PluginManager
{
    private static PluginManager? gPluginManager = null;
    public static PluginManager GetSingleton()
    {
        gPluginManager ??= new PluginManager();
        // Register the accessor for FindNeedlePluginLib
        PluginSubsystemAccessorProvider.Register(new PluginSubsystemAccessor(gPluginManager));
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
        try
        {
            FindPluginCore.Logger.Instance.Log($"CallFakeLoadPlugin called with plugin: {plugin}");
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly == null)
            {
                FindPluginCore.Logger.Instance.Log("Entry assembly is null");
                throw new Exception("Entry assembly is null");
            }

            var fakeLoaderPath = GetFakeLoadPluginPath();
            FindPluginCore.Logger.Instance.Log($"Using FakeLoadPlugin path: {fakeLoaderPath}");

            ProcessStartInfo ps = new()
            {
                FileName = fakeLoaderPath,
                Arguments = "\"" + plugin + "\"",
                WorkingDirectory = Path.GetDirectoryName(entryAssembly.Location) ?? throw new Exception("Failed to get directory of entry assembly"),
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
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
                    if (e.Data != null)
                        eOut += e.Data + "\n";
                });
                p.OutputDataReceived += new DataReceivedEventHandler((sender, e) =>
                {
                    if (e.Data != null)
                        eOut += e.Data + "\n";
                });
                p.EnableRaisingEvents = true;
            }
            FindPluginCore.Logger.Instance.Log($"Starting FakeLoadPlugin process for plugin: {plugin}");
            p.Start();
            if (GlobalSettings.Debug)
            {
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
            }

            p.WaitForExit();
            FindPluginCore.Logger.Instance.Log($"FakeLoadPlugin process exited for plugin: {plugin} with code {p.ExitCode}");
            if (GlobalSettings.Debug)
            {
                FindPluginCore.Logger.Instance.Log($"FakeLoadPlugin output: {eOut}");
            }
            return eOut;
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in CallFakeLoadPlugin: {ex}");
            throw;
        }
    }

    private readonly string loadedConfig = "";
    public PluginConfig? config;
    public List<InMemoryPluginModule> loadedPluginsModules = new();


    public void PrintToConsole()
    {
        FindPluginCore.Logger.Instance.Log($"Loaded ({loadedPluginsModules.Count}) plugin modules.");
    }


    public PluginManager(string configFileToLoad = "")
    {
        try
        {
            var originalConfig = configFileToLoad;
            if (string.IsNullOrEmpty(configFileToLoad))
            {
                configFileToLoad = LOADER_CONFIG;
            }

            configFileToLoad = FileIO.FindFullPathToFile(configFileToLoad, false); //Error handling happens later.

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
                if (!string.IsNullOrEmpty(originalConfig))
                {
                    var whatIcansee = Directory.EnumerateFiles(AppDomain.CurrentDomain.BaseDirectory).ToList();
                    throw new Exception("Config file was specified and it doesnt exist. " + whatIcansee.Count);
                }
                config = new PluginConfig();
            }
            loadedConfig = configFileToLoad;
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in PluginManager constructor: {ex}");
            throw;
        }
    }

    public void SaveToFile(string configFileToSave = "")
    {
        try
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
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in SaveToFile: {ex}");
            throw;
        }
    }



    public List<T> GetAllPluginsInstancesOfAType<T>()
    {
        List<T> ret = new();
        foreach (var pluginModule in loadedPluginsModules)
        {
            foreach (var plugin in pluginModule.description)
            {
                //Skip it if it doesnt implement the interface we need
                if (plugin.ImplementedInterfaces.FirstOrDefault(x => x.Equals(typeof(T).FullName)) == null)
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
        FindPluginCore.Logger.Instance.Log($"Starting to load plugins. Config entries: {(config?.entries.Count ?? 0)}");
        try
        {
            if (config != null)
            {
                foreach (var pluginModuleDescriptor in config.entries)
                {
                    FindPluginCore.Logger.Instance.Log($"Loading plugin module: {pluginModuleDescriptor.path}");
                    pluginModuleDescriptor.path = FileIO.FindFullPathToFile(pluginModuleDescriptor.path);
                    if (!File.Exists(pluginModuleDescriptor.path))
                    {
                        FindPluginCore.Logger.Instance.Log($"ERROR: Can't find plugin module for {pluginModuleDescriptor.path}");
                        throw new Exception($"Can't find plugin module for {pluginModuleDescriptor.path}");
                    }

                    InMemoryPluginModule loadedPluginModule = new(pluginModuleDescriptor.path, this, loadIntoAssembly);
                    loadedPluginsModules.Add(loadedPluginModule);
                    FindPluginCore.Logger.Instance.Log($"Loaded plugin module: {pluginModuleDescriptor.path}");
                }
                FindPluginCore.Logger.Instance.Log($"Finished loading plugins. Total loaded: {loadedPluginsModules.Count}");
            }
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in LoadAllPlugins: {ex}");
            throw;
        }
    }



    public string GetFakeLoadPluginPath()
    {
        try
        {
            config ??= new PluginConfig();
            if (String.IsNullOrEmpty(config.PathToFakeLoadPlugin))
            {
                config.PathToFakeLoadPlugin = FAKE_LOADER;
            }
            config.PathToFakeLoadPlugin = FileIO.FindFullPathToFile(config.PathToFakeLoadPlugin);
            if (!File.Exists(config.PathToFakeLoadPlugin))
            {
                throw new Exception("can't find fake loader for plugins");
            }
            return config.PathToFakeLoadPlugin;
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in GetFakeLoadPluginPath: {ex}");
            throw;
        }
    }

    public string GetSearchQueryClass()
    {
        try
        {
            config ??= new PluginConfig();
            if (String.IsNullOrEmpty(config.SearchQueryClass))
            {
                config.SearchQueryClass = "SearchQuery"; //Use old one by default
            }
            return config.SearchQueryClass;
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in GetSearchQueryClass: {ex}");
            throw;
        }
    }
    public static List<string> EnumerateFilesInCurrentDirectory()
    {
        return Directory.EnumerateFiles(Directory.GetCurrentDirectory()).ToList();
    }



}
