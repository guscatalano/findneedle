using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib.Interfaces;

namespace FindPluginCore.PluginSubsystem;


public class InMemoryPluginObject<T>
{
    public readonly InMemoryPluginModule plugin;
    public readonly PluginDescription description;
    public InMemoryPluginObject(InMemoryPluginModule plugin, PluginDescription description)
    {
        this.plugin = plugin;
        this.description = description;
    }

    public T? CreateInstance()
    {
        if (plugin.LoadedSuccessfully && plugin.dll != null)
        {
            var ret = (T?)plugin.dll.CreateInstance(description.ClassName);
            if(ret == null)
            {
                throw new Exception("Failed to create instance! maybe the classname is wrong?");
            }
            return ret;
        }
        else
        {
            throw new Exception("tried to load a badly loaded plugin");
        }
    }
}
