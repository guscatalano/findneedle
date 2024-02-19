using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FindNeedleUX.Services.WizardDef;
using Microsoft.UI.Xaml;

namespace FindNeedleUX.Services;

//Helps define the flow of selecting things without defining everyone by hand
public class WizardSelectionService
{
    public static WizardSelectionService g_wiz = new();
    public Dictionary<string, IWizard> wizards = new();
    public static IWizard current = null;

    public static WizardSelectionService GetInstance()
    {
        return g_wiz;
    }

    public static IWizard GetCurrentWizard()
    {
        return current;
    }

    public IWizard StartWizard(string name, UIElement sender, Action<string> callback)
    {
        current = wizards[name];
        current.StartWizard(sender, callback);
        return current;
    }

    public WizardSelectionService()
    {
        IWizard loc = new IWizard("FindNeedleUX.Windows.Location.LocationStart");
        
        loc.AddPage("LocationStart", new Dictionary<string, string>()
        {
            {"AddFile", "LocationAddFile" },
            {"AddEventLog", "LocationAddEventLog" },
            {"AddRegistry", "LocationAddRegistry" },
            {"AddVSO", "LocationAddVSO" }
        });
        wizards.Add("Location", loc);


        IWizard filter = new IWizard("FilterStart");

        filter.AddPage("FilterStart", new Dictionary<string, string>()
        {
            {"Keyword", "FilterAddSimpleKeyword" },
            {"TimeRange", "FilterAddTimeRange" },
            {"TimeAgo", "FilterAddTimeAgo" }
        });
        wizards.Add("Filter", filter);
    }
}
