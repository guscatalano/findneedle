using System;
using System.Collections.Generic;
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
}

public interface IPluginDescription
{
    public string GetTextDescription();
    public string GetFriendlyName();

    public string GetClassName();

    public static PluginDescription GetPluginDescription(IPluginDescription plugin, string sourceFile, 
                                                            List<string> implementedInterfaces, List<string> implementedInterfacesShort)
    {
        PluginDescription description = new PluginDescription()
        {
            TextDescription = plugin.GetTextDescription(),
            FriendlyName = plugin.GetFriendlyName(),
            ClassName = plugin.GetClassName(),
            SourceFile = sourceFile,
            ImplementedInterfaces = implementedInterfaces,
            ImplementedInterfacesShort = implementedInterfacesShort
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
