using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib.Interfaces;

namespace FindPluginCore.PluginSubsystem;


public class InMemoryPlugin
{
    public List<PluginDescription> description;
    public Assembly? dll = null;
    public bool LoadedSuccessfully = false;
    public Exception? LoadException = null;

    public InMemoryPlugin(string fullpath, List<PluginDescription> description)
    {
        try
        {
            dll = Assembly.LoadFile(fullpath);
            LoadedSuccessfully = true;
        }
        catch (Exception e)
        {
            LoadedSuccessfully = false;
            LoadException = e;
        }
        this.description = description;
    }

    public InMemoryPluginObject<object> GetObjectForType(PluginDescription desc)
    {
        return new InMemoryPluginObject<object>(this, desc);
    }



    public object? CreateInstance(PluginDescription specificDescription)
    {
        return GetObjectForType(specificDescription).CreateInstance();
    }

}
