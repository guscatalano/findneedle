// FindNeedle RuleDSL Home Page Code-Behind
// Primary page for RuleDSL-based search configuration

using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FindNeedleUX.Pages;

public sealed partial class RuleDSLHomePage : Page
{
    public RuleDSLHomePage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
    }

    private void OnAddLocationClicked(object sender, RoutedEventArgs e)
    {
        // Navigate to location selection page
        var rootFrame = Window.Current.Content as Frame;
        rootFrame?.Navigate(typeof(SearchLocationsPage));
    }

    private void OnNewRuleClicked(object sender, RoutedEventArgs e)
    {
        // Show dialog to create new rule
    }

    private void OnUmlDiagramClicked(object sender, RoutedEventArgs e)
    {
        // Navigate to UML diagram page
        var rootFrame = Window.Current.Content as Frame;
        rootFrame?.Navigate(typeof(DiagramToolsPage));
    }

    private async void OnBrowseRulesClicked(object sender, RoutedEventArgs e)
    {
        var window = Window.Current;
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

        picker.ViewMode = PickerViewMode.List;
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
        picker.FileTypeFilter.Add(".json");

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            // TODO: Load rules file
        }
    }

    private void OnAddFilterClicked(object sender, RoutedEventArgs e)
    {
        // Show dialog to add filter rule
    }

    private void OnAddEnrichmentClicked(object sender, RoutedEventArgs e)
    {
        // Show dialog to add enrichment rule
    }

    private void OnAddUmlClicked(object sender, RoutedEventArgs e)
    {
        // Show dialog to add UML rule
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        // Save pipeline configuration
    }

    private void OnTestClicked(object sender, RoutedEventArgs e)
    {
        // Test the pipeline configuration
    }

    private void OnRunSearchClicked(object sender, RoutedEventArgs e)
    {
        // Execute the search with the configured pipeline
        var rootFrame = Window.Current.Content as Frame;
        rootFrame?.Navigate(typeof(ResultsWebPage));
    }
}
