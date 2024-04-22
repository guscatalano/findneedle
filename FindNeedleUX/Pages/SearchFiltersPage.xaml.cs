using System.Collections.ObjectModel;
using FindNeedleUX.Services;
using FindNeedleUX.ViewObjects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Pages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class SearchFiltersPage : Page
{
    ObservableCollection<FilterListItem> RecipeList = new();
    public SearchFiltersPage()
    {
        this.InitializeComponent();

        RecipeList = MiddleLayerService.GetFilterListItems();
        VariedImageSizeRepeater.ItemsSource = RecipeList;
        
    }


    private void ButtonRemove_Click(object sender, RoutedEventArgs e)
    {

    }

    private void Button_Click_1(object sender, RoutedEventArgs e)
    {
        //((Button)sender)
    }


    private void Button_Click(object sender, RoutedEventArgs e)
    {

        WizardSelectionService.GetInstance().StartWizard("Filter", (UIElement)sender, Callback);

    }

    private void Callback(string test)
    {
        RecipeList = MiddleLayerService.GetFilterListItems();
        VariedImageSizeRepeater.ItemsSource = RecipeList;
    }
}
