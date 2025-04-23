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
}

public class PluginConfigEntry
{
    public string name = "";
    public string path = "";
    public bool enabled = true;
}
