using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib.Interfaces;

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
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "FindNeedlePlugin");
        Directory.CreateDirectory(folder);
        var fileName = Path.GetFileName(pluginPath) + ".json";
        return Path.Combine(folder, fileName);
    }

    private List<PluginDescription> LoadPluginDescriptors(string path, PluginManager pluginManager)
    {
        var descriptorFile = GetAppDataDescriptorFile(path);
        pluginManager.CallFakeLoadPlugin(path);
        if (File.Exists(descriptorFile))
        {
            return IPluginDescription.ReadDescriptionFile(descriptorFile);
        }
        else
        {
            throw new Exception("Plugin loader failed to load " + path);
        }
    }

    public InMemoryPluginModule(string fullpath, PluginManager pluginManager, bool loadInMemory = true)
    {
        try
        {
            description = LoadPluginDescriptors(fullpath, pluginManager);
            if (loadInMemory)
            {
                dll = Assembly.LoadFile(fullpath);
                LoadedSuccessfully = true;
            }
            else
            {
                LoadedSuccessfully = false;
                LoadExceptionString = "Constructor called with loadInMemory=false";
            }
        }
        catch (Exception e)
        {
            LoadedSuccessfully = false;
            LoadException = e;
            LoadExceptionString = e.Message;
            description = new List<PluginDescription>();
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
