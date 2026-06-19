using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedlePluginLib;

namespace findneedle.Implementations.Locations.EventLogQueryLocation;
public abstract class IEventLogQueryLocation : ISearchLocation, IPluginDescription
{
    // Concrete so the LocalEventLogLocation / FileEventLogQueryLocation / LocalEventLogQueryLocation
    // subclasses inherit a valid plugin description; the friendly name reflects the actual subclass.
    public virtual string GetPluginTextDescription() => "Reads Windows Event Log entries (live or saved)";
    public virtual string GetPluginFriendlyName() => GetType().Name;
    public string GetPluginClassName() => IPluginDescription.GetPluginClassNameBase(this);
}
