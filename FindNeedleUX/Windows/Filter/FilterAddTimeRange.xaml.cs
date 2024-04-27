using System;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Windows.Location;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class FilterAddTimeRange : Page
{
    public FilterAddTimeRange()
    {
        this.InitializeComponent();
    }
    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {
        
        DateTime actualStart = new DateTime(StartDate.Date.Year, StartDate.Date.Month, StartDate.Date.Day, StartTime.Time.Hours, StartTime.Time.Minutes, StartTime.Time.Seconds);
        DateTime actualEnd = new DateTime(EndDate.Date.Year, EndDate.Date.Month, EndDate.Date.Day, EndTime.Time.Hours, EndTime.Time.Minutes, EndTime.Time.Seconds);
        MiddleLayerService.AddTimeRangeFilter(actualStart, actualEnd);
        WizardSelectionService.GetCurrentWizard().NavigateNextOne("Quit");
    }
}
