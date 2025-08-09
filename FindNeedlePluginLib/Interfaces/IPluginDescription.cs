using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FindNeedlePluginLib;

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
    public string baseType;
}

public interface IPluginDescription
{
    public string GetPluginTextDescription();
    public string GetPluginFriendlyName();

    public string GetPluginClassName();

    public static string GetPluginClassNameBase(object thisplugin)
    {
        Type me = thisplugin.GetType();
        if (me.FullName == null)
        {
            throw new Exception("Fullname was null???");
        }
        else
        {
            return me.FullName;
        }
    }


    public static PluginDescription GetInvalidPluginDescription(string className, string baseType, string sourceFile,
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
            validationErrorMessage = error,
            baseType = baseType
        };
        return description;
    }

    public static PluginDescription GetPluginDescription(IPluginDescription pluginInstance, string baseType, string sourceFile,
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
            validationErrorMessage = "",
            baseType = baseType
        };
        return description;
    }

    public static void WriteDescriptionFile(List<PluginDescription> validTypes, string sourceFile, string outputfile)
    {
        var text = JsonSerializer.Serialize(validTypes, new JsonSerializerOptions
        {
            IncludeFields = true,

        });
        File.WriteAllText(outputfile, text);
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
