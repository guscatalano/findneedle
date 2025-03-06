using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FindPluginCore.PluginSubsystem
{
    public class PluginConfig
    {
        public List<PluginConfigEntry> entries = new();
        public string PathToFakeLoadPlugin = "";
    }

    public class PluginConfigEntry
    {
        public string name;
        public string path;
    }
}
