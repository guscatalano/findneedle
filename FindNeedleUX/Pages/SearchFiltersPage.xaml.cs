using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using FindNeedleUX.Services;
using FindNeedleUX.TestMocks;
using FindNeedleUX.ViewObjects;
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

namespace FindNeedleUX.Pages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class SearchFiltersPage : Page
{
    List<FilterListItem> RecipeList = new List<FilterListItem>();
    public SearchFiltersPage()
    {
        this.InitializeComponent();
       // RecipeList.Add(new FilterListItem() {  Name = RandomData.GetRandomFilterName(), Description= RandomData.GetName() });
      //  RecipeList.Add(new FilterListItem() { Name = RandomData.GetRandomFilterName(), Description = RandomData.GetName() });
       // RecipeList.Add(new FilterListItem() { Name = RandomData.GetRandomFilterName(), Description = RandomData.GetName() });
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
        //RecipeList = MiddleLayerService.GetLocationListItems();
        //VariedImageSizeRepeater.ItemsSource = RecipeList;
    }
}
