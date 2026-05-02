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


    private void Button_Remove(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string name })
        {
            MiddleLayerService.RemoveFilterByName(name);
            RecipeList = MiddleLayerService.GetFilterListItems();
            VariedImageSizeRepeater.ItemsSource = RecipeList;
        }
    }

private void Callback(string test)
    {
        RecipeList = MiddleLayerService.GetFilterListItems();
        VariedImageSizeRepeater.ItemsSource = RecipeList;
        MiddleLayerService.NotifyStateChanged();
    }
}
