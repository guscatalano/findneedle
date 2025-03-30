using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FindNeedlePluginLib.Interfaces;

public struct PluginDescription
{
    public string TextDescription;
    public string FriendlyName;
    public string ClassName;
    public string SourceFile;
    public List<string> ImplementedInterfaces;
    public List<string> ImplementedInterfacesShort;
    public bool validPlugin;
    public string validationErrorMessage;
}

public interface IPluginDescription
{
    public string GetPluginTextDescription();
    public string GetPluginFriendlyName();

    public string GetPluginClassName();


    public static PluginDescription GetInvalidPluginDescription(string className, string sourceFile,
                                                            List<string> implementedInterfaces, List<string> implementedInterfacesShort, string error)
    {
        PluginDescription description = new PluginDescription()
        {
            TextDescription = "Invalid",
            FriendlyName = "Invalid",
            ClassName = className,
            SourceFile = sourceFile,
            ImplementedInterfaces = implementedInterfaces,
            ImplementedInterfacesShort = implementedInterfacesShort,
            validPlugin = false,
            validationErrorMessage = error
        };
        return description;
    }

    public static PluginDescription GetPluginDescription(IPluginDescription pluginInstance, string sourceFile, 
                                                            List<string> implementedInterfaces, List<string> implementedInterfacesShort)
    {
        PluginDescription description = new PluginDescription()
        {
            TextDescription = pluginInstance.GetPluginTextDescription(),
            FriendlyName = pluginInstance.GetPluginFriendlyName(),
            ClassName = pluginInstance.GetPluginClassName(),
            SourceFile = sourceFile,
            ImplementedInterfaces = implementedInterfaces,
            ImplementedInterfacesShort = implementedInterfacesShort,
            validPlugin = true,
            validationErrorMessage = ""
        };
        return description;
    }

    public static void WriteDescriptionFile(List<PluginDescription> validTypes, string sourceFile)
    {
        var text = JsonSerializer.Serialize(validTypes, new JsonSerializerOptions
        {
            IncludeFields = true,

        });
        File.WriteAllText(sourceFile + ".json", text);
    }

    public static List<PluginDescription> ReadDescriptionFile(string inputFile)
    {
        var inputText = File.ReadAllText(inputFile);
        var output = JsonSerializer.Deserialize<List<PluginDescription>>(inputText, new JsonSerializerOptions
        {
            IncludeFields = true,

        });
        if(output == null)
        {
            throw new Exception("Invalid descriptor file");
        }
        return output;
    }


}
