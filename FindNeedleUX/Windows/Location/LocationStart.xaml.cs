using System;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIEx.Messaging;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Windows.Location;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class LocationStart : Page
{
    private string selectedItem = "";
    public LocationStart()
    {
        this.InitializeComponent();
        WizardSelectionService.GetCurrentWizard().RegisterCurrentPage(this);
    }
    private void Button_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            WizardSelectionService.GetCurrentWizard().NavigateNextOne(selectedItem);
        }
        catch (Exception)
        {
            ShowErrorDialogAsync("Page doesn't exist yet...");
        }
    }

    private void RadioButton_Checked(object sender, RoutedEventArgs e)
    {
        selectedItem = (sender as RadioButton).Name.ToString();
    }
    private async Task ShowErrorDialogAsync(string message)
    {
        var dialog = new ContentDialog
        {
            Title = "Error",
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot // Ensure dialog is shown on the correct thread/root
        };
        await dialog.ShowAsync();
    }
}
