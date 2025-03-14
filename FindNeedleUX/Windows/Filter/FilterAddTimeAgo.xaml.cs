using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using findneedle.Implementations;
using FindNeedleCoreUtils;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Windows.Filter;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class FilterAddTimeAgo : Page
{
    public FilterAddTimeAgo()
    {
        this.InitializeComponent();
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        
        var count = Int32.Parse(UnitCount.Text);
        TimeAgoUnit actualUnit = TimeAgoUnit.Second;
        switch (Unit.SelectedValue.ToString().ToLower())
        {
            case "seconds":
                actualUnit = TimeAgoUnit.Second;
            break;
            case "minutes":
                actualUnit = TimeAgoUnit.Minute;
            break;
            case "hours":
                actualUnit = TimeAgoUnit.Hour;
            break;
            case "days":
                actualUnit = TimeAgoUnit.Day;
            break;
        }
        MiddleLayerService.AddTimeAgoFilter(actualUnit, count);
        WizardSelectionService.GetCurrentWizard().NavigateNextOne("Quit");
    }
}
