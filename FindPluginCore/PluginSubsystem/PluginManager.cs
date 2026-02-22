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
using Microsoft.Win32;
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
            FindNeedlePluginLib.Logger.Instance.Log($"CallFakeLoadPlugin called with plugin: {plugin}");
            var entryAssembly = Assembly.GetEntryAssembly();
            if (entryAssembly == null)
            {
                FindNeedlePluginLib.Logger.Instance.Log("Entry assembly is null");
                throw new Exception("Entry assembly is null");
            }

            var fakeLoaderPath = GetFakeLoadPluginPath();
            FindNeedlePluginLib.Logger.Instance.Log($"Using FakeLoadPlugin path: {fakeLoaderPath}");

            ProcessStartInfo ps = new()
            {
                FileName = fakeLoaderPath,
                Arguments = "\"" + plugin + "\"",
                WorkingDirectory = Path.GetDirectoryName(entryAssembly.Location) ?? throw new Exception("Failed to get directory of entry assembly"),
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden,
                // Always redirect so we can capture child process output and decide where to route it
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            var outputBuffer = new StringBuilder();
            Process p = new Process();
            p.StartInfo = ps;

            // Always capture output and error from the child process
            p.ErrorDataReceived += new DataReceivedEventHandler((sender, e) =>
            {
                if (e.Data == null) return;
                outputBuffer.AppendLine(e.Data);
                // Route into main logger only; avoid noisy console output
                FindNeedlePluginLib.Logger.Instance.Log($"FakeLoadPlugin STDERR: {e.Data}");
            });

            p.OutputDataReceived += new DataReceivedEventHandler((sender, e) =>
            {
                if (e.Data == null) return;
                outputBuffer.AppendLine(e.Data);
                // Route into main logger only; avoid noisy console output
                FindNeedlePluginLib.Logger.Instance.Log($"FakeLoadPlugin STDOUT: {e.Data}");
            });

            p.EnableRaisingEvents = true;

            FindNeedlePluginLib.Logger.Instance.Log($"Starting FakeLoadPlugin process for plugin: {plugin}");
            p.Start();
            // Begin asynchronous read so handlers receive data
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            p.WaitForExit();
            FindNeedlePluginLib.Logger.Instance.Log($"FakeLoadPlugin process exited for plugin: {plugin} with code {p.ExitCode}");
            // If not in debug, we still saved outputBuffer into the logger line-by-line above

            // Also try to read the FakeLoadPlugin output file (written to AppData) and append to our logger
            try
            {
                var appDataFolder = FileIO.GetAppDataFindNeedlePluginFolder();
                var fakeOutputPath = Path.Combine(appDataFolder, "fakeloadplugin_output.txt");
                if (File.Exists(fakeOutputPath))
                {
                    var lines = File.ReadAllLines(fakeOutputPath);
                    foreach (var l in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(l))
                        {
                            FindNeedlePluginLib.Logger.Instance.Log($"FakeLoadPlugin: {l}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FindNeedlePluginLib.Logger.Instance.Log($"Failed to read FakeLoadPlugin output file: {ex.Message}");
            }
            return outputBuffer.ToString();
        }
        catch (Exception ex)
        {
            FindNeedlePluginLib.Logger.Instance.Log($"Exception in CallFakeLoadPlugin: {ex}");
            throw;
        }
    }

    private readonly string loadedConfig = "";
    public PluginConfig? config;
    public List<InMemoryPluginModule> loadedPluginsModules = new();


    public void PrintToConsole()
    {
        FindNeedlePluginLib.Logger.Instance.Log($"Loaded ({loadedPluginsModules.Count}) plugin modules.");
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
            FindNeedlePluginLib.Logger.Instance.Log($"Exception in PluginManager constructor: {ex}");
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
            FindNeedlePluginLib.Logger.Instance.Log($"Exception in SaveToFile: {ex}");
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
        FindNeedlePluginLib.Logger.Instance.Log($"Starting to load plugins. Config entries: {(config?.entries.Count ?? 0)}");
        try
        {
            if (config != null)
            {
                // --- BEGIN: Registry plugin loading ---
                if (config.UserRegistryPluginKeyEnabled && !string.IsNullOrWhiteSpace(config.UserRegistryPluginKey))
                {
                    try
                    {
                        // Open registry key with minimal permissions (read-only, non-writable)
                        FindNeedlePluginLib.Logger.Instance.Log($"Attempting to open registry key: HKCU\\{config.UserRegistryPluginKey}");
                        using var regKey = Registry.CurrentUser.OpenSubKey(config.UserRegistryPluginKey, writable: false);
                        FindNeedlePluginLib.Logger.Instance.Log($"Process: {Process.GetCurrentProcess().ProcessName}, Is64Bit: {Environment.Is64BitProcess}, AppDomain: {AppDomain.CurrentDomain.FriendlyName}");
                        if (regKey == null)
                        {
                            FindNeedlePluginLib.Logger.Instance.Log($"Registry key not found: HKCU\\{config.UserRegistryPluginKey}");
                        }
                        else
                        {
                            var value = regKey.GetValue("") as string;
                            FindNeedlePluginLib.Logger.Instance.Log($"Registry key found. Value: '{value ?? "<null>"}'");
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                var plugins = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                foreach (var pluginPath in plugins)
                                {
                                    // Only add if not already present
                                    if (!config.entries.Any(e => string.Equals(e.path, pluginPath, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        config.entries.Add(new PluginConfigEntry { name = Path.GetFileNameWithoutExtension(pluginPath), path = pluginPath, enabled = true });
                                        FindNeedlePluginLib.Logger.Instance.Log($"Loaded plugin from registry: {pluginPath}");
                                    }
                                }
                            }
                            else
                            {
                                FindNeedlePluginLib.Logger.Instance.Log($"Registry value is empty or whitespace for key: HKCU\\{config.UserRegistryPluginKey}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        FindNeedlePluginLib.Logger.Instance.Log($"Error reading plugins from registry: {ex.Message}");
                    }
                }
                // --- END: Registry plugin loading ---
                foreach (var pluginModuleDescriptor in config.entries)
                {
                    try
                    {
                        FindNeedlePluginLib.Logger.Instance.Log($"Loading plugin module: {pluginModuleDescriptor.path}");
                        var originalPath = pluginModuleDescriptor.path;
                        pluginModuleDescriptor.path = FileIO.FindFullPathToFile(pluginModuleDescriptor.path);
                        if (!File.Exists(pluginModuleDescriptor.path))
                        {
                            // Try to locate the plugin under the application's base directory (including subfolders)
                            try
                            {
                                var fileName = Path.GetFileName(originalPath);
                                if (!string.IsNullOrEmpty(fileName))
                                {
                                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                                    var candidate = Directory.EnumerateFiles(baseDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                                    if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                                    {
                                        pluginModuleDescriptor.path = candidate;
                                        FindNeedlePluginLib.Logger.Instance.Log($"Resolved plugin {fileName} to {candidate}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                FindNeedlePluginLib.Logger.Instance.Log($"Error searching for plugin {originalPath}: {ex.Message}");
                            }

                            if (!File.Exists(pluginModuleDescriptor.path))
                            {
                                // Automatically mark as disabled and record reason in the config entry (if supported)
                                pluginModuleDescriptor.enabled = false;
                                try
                                {
                                    // If the config entry has a disabledReason field, set it (handles both dynamic and typed cases)
                                    var prop = pluginModuleDescriptor.GetType().GetProperty("disabledReason");
                                    if (prop != null)
                                    {
                                        prop.SetValue(pluginModuleDescriptor, "File not found: " + originalPath);
                                    }
                                }
                                catch { }

                                FindNeedlePluginLib.Logger.Instance.Log($"Auto-disabling missing plugin module: {originalPath}");
                                continue;
                            }
                        }

                        InMemoryPluginModule loadedPluginModule = new(pluginModuleDescriptor.path, this, loadIntoAssembly);
                        loadedPluginsModules.Add(loadedPluginModule);
                        FindNeedlePluginLib.Logger.Instance.Log($"Loaded plugin module: {pluginModuleDescriptor.path}");
                    }
                    catch (Exception ex)
                    {
                        FindNeedlePluginLib.Logger.Instance.Log($"WARNING: Failed to load plugin module {pluginModuleDescriptor.path}: {ex.Message}");
                        // Continue loading other plugins
                    }
                }
                FindNeedlePluginLib.Logger.Instance.Log($"Finished loading plugins. Total loaded: {loadedPluginsModules.Count}");
            }
        }
        catch (Exception ex)
        {
            FindNeedlePluginLib.Logger.Instance.Log($"Exception in LoadAllPlugins: {ex}");
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
            FindNeedlePluginLib.Logger.Instance.Log($"Exception in GetFakeLoadPluginPath: {ex}");
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
            FindNeedlePluginLib.Logger.Instance.Log($"Exception in GetSearchQueryClass: {ex}");
            throw;
        }
    }
    public static List<string> EnumerateFilesInCurrentDirectory()
    {
        return Directory.EnumerateFiles(Directory.GetCurrentDirectory()).ToList();
    }



}
