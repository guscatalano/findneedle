using System.Collections.ObjectModel;
using FindNeedleUX.Services;
using FindNeedleUX.Utils;
using FindNeedleUX.ViewObjects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Pages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class SearchLocationsPage : Page
{
    ObservableCollection<LocationListItem> RecipeList = new();
    public SearchLocationsPage()
    {
        this.InitializeComponent();
        CheckOtherDLLs.AreWeInstalledOk();
        RecipeList = MiddleLayerService.GetLocationListItems();
        VariedImageSizeRepeater.ItemsSource = RecipeList;

    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {

        WizardSelectionService.GetInstance().StartWizard("Location", (UIElement)sender, Callback);

    }

    private void Callback(string test)
    {
        RecipeList = MiddleLayerService.GetLocationListItems();
        VariedImageSizeRepeater.ItemsSource = RecipeList;
    }



}
