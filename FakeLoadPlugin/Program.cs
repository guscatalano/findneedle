using System;
using System.Reflection;
using System.Text.Json;
using FindNeedlePluginLib.Interfaces;

namespace FakeLoadPlugin;


public class Program
{

    static void Main(string[] args)
    {
        var output = LoadPluginModule(args[0]);
        IPluginDescription.WriteDescriptionFile(output, args[0]);
    }

    public static List<PluginDescription> LoadPluginModule(string file)
    {
        Console.WriteLine("Attempting to load: " + file);
        var dll = Assembly.LoadFile(file);
        var types = dll.GetTypes();
        List<PluginDescription> foundTypes = new List<PluginDescription>();
        
        foreach (var type in types)
        {
            var instantiate = false;
            if (type.FullName == null)
            {
                continue;
            }
            List<string> implementedInterfaces = new();
            List<string> implementedInterfacesShort = new();
            Console.WriteLine("Found class: " + type.FullName);
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
                    Console.WriteLine("Friendly Name: " + ((IPluginDescription)Plugin).GetPluginFriendlyName());
                    Console.WriteLine("Description: " + ((IPluginDescription)Plugin).GetPluginTextDescription());
                    foundTypes.Add(IPluginDescription.GetPluginDescription((IPluginDescription)Plugin, 
                        file, implementedInterfaces, implementedInterfacesShort));
                } 
                else
                {
                    Console.WriteLine("Failed to create instance of " + type.FullName);
                    foundTypes.Add(IPluginDescription.GetInvalidPluginDescription(type.FullName,
                    file, implementedInterfaces, implementedInterfacesShort, "Could not instantiate (missing binary?)"));
                }
            } 
            else
            {
                Console.WriteLine("Skipping " + type.FullName + " because it has no IPluginDescription");
                foundTypes.Add(IPluginDescription.GetInvalidPluginDescription(type.FullName, 
                    file, implementedInterfaces, implementedInterfacesShort, "No IPluginDescription"));
            }
        }
        return foundTypes;
    }
}