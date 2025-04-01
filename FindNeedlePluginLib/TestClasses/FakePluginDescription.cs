using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib.Interfaces;

namespace FindNeedlePluginLib.TestClasses;

[ExcludeFromCodeCoverage]
public class FakePluginDescription : IPluginDescription
{
    public string GetPluginClassName()
    {
        return IPluginDescription.GetPluginClassNameBase(this);
    }

    public string friendlyname = "fakefriend";
    public string textdescription = "fakedescription";

    public string GetPluginFriendlyName()
    {
        return friendlyname;
    }
    public string GetPluginTextDescription()
    {
        return textdescription;
    }
}
