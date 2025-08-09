using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindPluginCore.PluginSubsystem;

[ExcludeFromCodeCoverage]
public class PluginConfig
{
    public List<PluginConfigEntry> entries = new();
    public string PathToFakeLoadPlugin = "";
    public string SearchQueryClass = "";
    public string PlantUMLPath = string.Empty; // Optional: Path to PlantUML JAR
    public string UserRegistryPluginKey = string.Empty; // Optional: Registry key in HKCU for extra plugins
    public bool UserRegistryPluginKeyEnabled = false; // Enable/disable registry plugin loading
    public bool UseSynchronousSearch = false; // Add this option
}

public class PluginConfigEntry
{
    public string name = "";
    public string path = "";
    public bool enabled = true;
}
