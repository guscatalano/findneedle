using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib;
using FindNeedleCoreUtils;
using FindPluginCore;
using static FindNeedlePluginLib.Logger;

namespace FindPluginCore.PluginSubsystem;

public class InMemoryPluginModule
{
    public List<PluginDescription> description;
    public Assembly? dll = null;
    public bool LoadedSuccessfully = false;
    public Exception? LoadException = null;
    public string LoadExceptionString = "";

    private static string GetAppDataDescriptorFile(string pluginPath)
    {
        var folder = FileIO.GetAppDataFindNeedlePluginFolder();
        var fileName = Path.GetFileName(pluginPath) + ".json";
        return Path.Combine(folder, fileName);
    }

    private List<PluginDescription> LoadPluginDescriptors(string path, PluginManager pluginManager)
    {
        var descriptorFile = GetAppDataDescriptorFile(path);
        Logger.Instance.Log($"InMemoryPluginModule: Loading plugin descriptors for {path}");
        try
        {
            pluginManager.CallFakeLoadPlugin(path);
            if (File.Exists(descriptorFile))
            {
                Logger.Instance.Log($"InMemoryPluginModule: Descriptor file found for {path}");
                return IPluginDescription.ReadDescriptionFile(descriptorFile);
            }
            else
            {
                Logger.Instance.Log($"InMemoryPluginModule: Descriptor file NOT found for {path}");
                throw new Exception("Plugin loader failed to load " + path);
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"InMemoryPluginModule: Exception in LoadPluginDescriptors for {path}: {ex}");
            throw;
        }
    }

    public InMemoryPluginModule(string fullpath, PluginManager pluginManager, bool loadInMemory = true)
    {
        Logger.Instance.Log($"InMemoryPluginModule: Creating for {fullpath}, loadInMemory={loadInMemory}");
        try
        {
            description = LoadPluginDescriptors(fullpath, pluginManager);
            if (loadInMemory)
            {
                dll = Assembly.LoadFile(fullpath);
                LoadedSuccessfully = true;
                Logger.Instance.Log($"InMemoryPluginModule: Loaded assembly for {fullpath}");
            }
            else
            {
                LoadedSuccessfully = false;
                LoadExceptionString = "Constructor called with loadInMemory=false";
                Logger.Instance.Log($"InMemoryPluginModule: Not loading assembly for {fullpath} (loadInMemory=false)");
            }
        }
        catch (Exception e)
        {
            LoadedSuccessfully = false;
            LoadException = e;
            LoadExceptionString = e.Message;
            description = new List<PluginDescription>();
            Logger.Instance.Log($"InMemoryPluginModule: Exception in constructor for {fullpath}: {e}");
        }
    }

    public InMemoryPluginObject<object> GetObjectForTypeGeneric(PluginDescription desc)
    {
        return new InMemoryPluginObject<object>(this, desc);
    }

    public InMemoryPluginObject<T> GetObjectForType<T>(PluginDescription desc)
    {
        return new InMemoryPluginObject<T>(this, desc);
    }
}
