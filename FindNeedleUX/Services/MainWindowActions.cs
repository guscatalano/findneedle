using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.CompilerServices;

namespace FindNeedleUX.Services;
public class MainWindowActions
{
    private static NavigationView navigationView;
    public static void DisableNavBar()
    {
        navigationView.IsEnabled = false;
    }

    public static void EnableNavBar()
    {
        navigationView.IsEnabled = true;
    }

    public static void TrackNavBar(NavigationView bar)
    {
            navigationView = bar;
    }

}
