using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations;
using FindNeedleUX.Services.WizardDef;
using FindNeedleUX.ViewObjects;
using Microsoft.UI.Xaml.Controls;

namespace FindNeedleUX.Services;
public class MiddleLayerService
{
    public static List<SearchLocation> Locations = new();

    public static void AddFolderLocation(string location)
    {
        Locations.Add(new FolderLocation(location));
    }

    public static void PageChanged(IWizard wizard, Page current)
    {

    }

    public static ObservableCollection<LocationListItem> GetLocationListItems()
    {
        ObservableCollection<LocationListItem> test = new ObservableCollection<LocationListItem>();
        foreach (SearchLocation loc in Locations)
        {
            if (loc.GetType() == typeof(FolderLocation))
            {
                FolderLocation x = (FolderLocation)loc;
                test.Add(new LocationListItem() { Name = x.GetName(), Description=x.GetDescription() });
            }
            
        }
        return test;
    }
}
