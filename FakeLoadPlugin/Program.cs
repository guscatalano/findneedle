using System;
using System.Reflection;
using System.Text.Json;
using FindNeedlePluginLib.Interfaces;

namespace FakeLoadPlugin;


internal class Program
{

    static void Main(string[] args)
    {
        var output = LoadPlugin(args[0]);
        IPluginDescription.WriteDescriptionFile(output, args[0]);
    }

    private static List<PluginDescription> LoadPlugin(string file)
    {
        Console.WriteLine("Attempting to load: " + file);
        var dll = Assembly.LoadFile(file);
        var types = dll.GetTypes();
        List<PluginDescription> validTypes = new List<PluginDescription>();
        
        foreach (var type in types)
        {
            var instantiate = false;
            if (type.FullName == null)
            {
                continue;
            }
            List<string> implementedInterfaces = new();
            List<string> implementedInterfacesShort = new();
            foreach (var possibleInterface in type.GetInterfaces())
            {
                
                if(possibleInterface.FullName == null)
                {
                    continue;
                }

                Console.WriteLine("Found interface: " + possibleInterface.FullName);
                if (possibleInterface.FullName.Equals("FindNeedlePluginLib.Interfaces.IPluginDescription"))
                {
                    instantiate = true;
                    Console.WriteLine("Found potential plugin, loading...");
                }
                implementedInterfaces.Add(possibleInterface.FullName);
                implementedInterfacesShort.Add(possibleInterface.Name);
            }

            if (instantiate)
            {
                var Plugin = dll.CreateInstance(type.FullName);
                if (Plugin != null)
                {
                    Console.WriteLine("Friendly Name: " + ((IPluginDescription)Plugin).GetFriendlyName());
                    Console.WriteLine("Description: " + ((IPluginDescription)Plugin).GetTextDescription());
                    validTypes.Add(IPluginDescription.GetPluginDescription((IPluginDescription)Plugin, file, implementedInterfaces, implementedInterfacesShort));
                }
            }
        }
        return validTypes;
    }
}