using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib.Interfaces;

namespace FindPluginCore.PluginSubsystem;


public class InMemoryPluginObject<T>
{
    private readonly InMemoryPluginModule plugin;
    private PluginDescription description;
    public InMemoryPluginObject(InMemoryPluginModule plugin, PluginDescription description)
    {
        this.plugin = plugin;
        this.description = description;
    }

    public T? CreateInstance()
    {
        if (plugin.LoadedSuccessfully && plugin.dll != null)
        {
            return (T?)plugin.dll.CreateInstance(description.ClassName);
        }
        else
        {
            throw new Exception("tried to load a badly loaded plugin");
        }
    }
}
