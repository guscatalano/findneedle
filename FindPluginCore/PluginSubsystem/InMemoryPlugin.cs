using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib.Interfaces;

namespace FindPluginCore.PluginSubsystem
{
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

        public object? CreateInstance(PluginDescription specificDescription)
        {
            if (LoadedSuccessfully && dll != null) { 
                return dll.CreateInstance(specificDescription.ClassName);
            } 
            else
            {
                throw new Exception("tried to load a badly loaded plugin");
            }
        }

    }
}
