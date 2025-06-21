using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using FindNeedlePluginLib.Interfaces;
using FindNeedleCoreUtils;

namespace FakeLoadPlugin;

public class Program
{
    static readonly string OUTPUT_FILE = "fakeloadplugin_output.txt";
    static readonly string APPDATA_SUBFOLDER = "FindNeedlePlugin";

    static string GetAppDataFolder()
    {
        return FileIO.GetAppDataFindNeedlePluginFolder();
    }

    static string GetOutputFilePath()
    {
        return Path.Combine(GetAppDataFolder(), OUTPUT_FILE);
    }

    static string GetOutputFilePath(string filename)
    {
        return Path.Combine(GetAppDataFolder(), filename);
    }

    [ExcludeFromCodeCoverage]
    static void Main(string[] args)
    {
        var logPath = GetOutputFilePath();
        File.WriteAllText(logPath, "Starting at " + DateTime.Now.ToString() + Environment.NewLine); //Clear log file
        if (args.Count() == 0)
        {
            WriteToConsoleAndFile("No arguments were passed");
            Environment.Exit(-1);
        }
        try
        {
            var sourcefile = args[0];
            var descriptor = LoadPluginModule(sourcefile);
            var outputfile = GetOutputFilePath(Path.GetFileName(sourcefile) + ".json");
            IPluginDescription.WriteDescriptionFile(descriptor, sourcefile, outputfile);
            WriteToConsoleAndFile("Wrote descriptor successfully to: " + outputfile);
        }
        catch (Exception e)
        {
            try
            {
                WriteToConsoleAndFile("Crashed: " + e);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Failed to write to file: " + ex);
                Environment.Exit(-5);
            }
        }
    }

    [ExcludeFromCodeCoverage]
    public static void WriteToConsoleAndFile(string text)
    {
        Console.WriteLine(text);
        var logPath = GetOutputFilePath();
        File.AppendAllText(logPath, text + Environment.NewLine);
    }

    public static List<PluginDescription> LoadPluginModule(string file)
    {
        WriteToConsoleAndFile("Attempting to load: " + file);
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
            WriteToConsoleAndFile("Found class: " + type.FullName);
            foreach (var possibleInterface in type.GetInterfaces())
            {

                if (possibleInterface.FullName == null)
                {
                    continue;
                }

                WriteToConsoleAndFile("Found interface: " + possibleInterface.FullName);
                if (possibleInterface.FullName.Equals("FindNeedlePluginLib.Interfaces.IPluginDescription"))
                {
                    instantiate = true;
                    WriteToConsoleAndFile("Found potential plugin, loading...");
                }
                implementedInterfaces.Add(possibleInterface.FullName);
                implementedInterfacesShort.Add(possibleInterface.Name);
            }

            if (instantiate)
            {
                var Plugin = dll.CreateInstance(type.FullName);
                if (Plugin != null)
                {
                    WriteToConsoleAndFile("Friendly Name: " + ((IPluginDescription)Plugin).GetPluginFriendlyName());
                    WriteToConsoleAndFile("Description: " + ((IPluginDescription)Plugin).GetPluginTextDescription());
                    foundTypes.Add(IPluginDescription.GetPluginDescription((IPluginDescription)Plugin,
                        file, implementedInterfaces, implementedInterfacesShort));
                }
                else
                {
                    WriteToConsoleAndFile("Failed to create instance of " + type.FullName);
                    foundTypes.Add(IPluginDescription.GetInvalidPluginDescription(type.FullName,
                    file, implementedInterfaces, implementedInterfacesShort, "Could not instantiate (missing binary?)"));
                }
            }
            else
            {
                WriteToConsoleAndFile("Skipping " + type.FullName + " because it has no IPluginDescription");
                foundTypes.Add(IPluginDescription.GetInvalidPluginDescription(type.FullName,
                    file, implementedInterfaces, implementedInterfacesShort, "No IPluginDescription"));
            }
        }
        return foundTypes;
    }
}