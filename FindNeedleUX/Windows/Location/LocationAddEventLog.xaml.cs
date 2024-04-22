using System.Collections.Generic;
using System.Linq;
using findneedle.Implementations.Discovery;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Windows.Location;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class LocationAddEventLog : Page
{
    public LocationAddEventLog()
    {
        this.InitializeComponent();
        WizardSelectionService.GetCurrentWizard().RegisterCurrentPage(this);

        eventlognames = EventLogDiscovery.GetAllEventLogs();
    }

    private readonly List<string> eventlognames;
    private void AutoSuggestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        // Since selecting an item will also change the text,
        // only listen to changes caused by user entering text.
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var suitableItems = new List<string>();
            var splitText = sender.Text.ToLower().Split(" ");
            foreach (var cat in eventlognames)
            {
                var found = splitText.All((key) =>
                {
                    return cat.ToLower().Contains(key);
                });
                if (found)
                {
                    suitableItems.Add(cat);
                }
            }
            if (suitableItems.Count == 0)
            {
                suitableItems.Add("No results found");
            }
            sender.ItemsSource = suitableItems;
        }
    }

    private void AutoSuggestBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        SuggestionOutput.Text = args.SelectedItem.ToString();
    }

    private void RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as RadioButton).Content.ToString().Equals("Everything"))
        {
            SuggestionBox.IsEnabled = false;
            SuggestionBox.Text = "Everything";
            SuggestionOutput.Text = "Everything";
        }
        else
        {
            SuggestionBox.IsEnabled = true;
            SuggestionBox.Text = "";
            SuggestionOutput.Text = "";
        }
    }

    private bool EventLogQuerySelected = false;
    private void RadioButton2_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as RadioButton).Content.ToString().Contains("Query"))
        {
            EventLogQuerySelected = true;
        }
        else
        {
            EventLogQuerySelected = false;
        }
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {

        MiddleLayerService.AddEventLog(SuggestionOutput.Text, EventLogQuerySelected);
        WizardSelectionService.GetCurrentWizard().NavigateNextOne("Quit");
    }
}
