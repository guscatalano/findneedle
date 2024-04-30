using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Windows;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class LocationAddFile : Page
{
    string currentSelection = "None";
    public LocationAddFile()
    {
        this.InitializeComponent();
        WizardSelectionService.GetCurrentWizard().RegisterCurrentPage(this);
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {

        if(currentSelection.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        MiddleLayerService.AddFolderLocation(currentSelection);
        WizardSelectionService.GetCurrentWizard().NavigateNextOne("Quit");
    }


    private async void PickAFileButton_Click(object sender, RoutedEventArgs e)
    {
        // Clear previous returned file name, if it exists, between iterations of this scenario
        OutputTextBlock.Text = "";



        // Retrieve the window handle (HWND) of the current WinUI 3 window.
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);



        var picker = new FileOpenPicker()
        {
            ViewMode = PickerViewMode.List,
            FileTypeFilter = { ".txt", ".etl", ".log" },
        };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

        var file = await picker.PickSingleFileAsync();
        // var files = await picker.PickMultipleFilesAsync();

        // Initialize the file picker with the window handle (HWND).
        // WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

        // Set options for your file picker
        // openPicker.ViewMode = PickerViewMode.Thumbnail;
        //  openPicker.FileTypeFilter.Add("*");

        // Open the picker for the user to pick a file
        //  var file = await openPicker.PickSingleFileAsync();
        if (file != null)
        {
            OutputTextBlock.Text = "Picked file: " + file.Path;
            currentSelection = file.Path;
        }
        else
        {
            OutputTextBlock.Text = "Operation cancelled.";
        }
    }

    private async void PickAFolderButton_Click(object sender, RoutedEventArgs e)
    {
        // Clear previous returned file name, if it exists, between iterations of this scenario
        OutputTextBlock.Text = "";



        // Retrieve the window handle (HWND) of the current WinUI 3 window.
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);



        var picker = new FolderPicker()
        {
            ViewMode = PickerViewMode.List,
        };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

        var file = await picker.PickSingleFolderAsync();
        // var files = await picker.PickMultipleFilesAsync();

        // Initialize the file picker with the window handle (HWND).
        // WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hWnd);

        // Set options for your file picker
        // openPicker.ViewMode = PickerViewMode.Thumbnail;
        //  openPicker.FileTypeFilter.Add("*");

        // Open the picker for the user to pick a file
        //  var file = await openPicker.PickSingleFileAsync();
        if (file != null)
        {
            OutputTextBlock.Text = "Picked folder: " + file.Path;
            currentSelection = file.Path;
        }
        else
        {
            OutputTextBlock.Text = "Operation cancelled.";
        }
    }

    private void CommonList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OutputTextBlock != null)
        {
            if (CommonList.SelectedItem.ToString().Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                OutputTextBlock.Text = "Nothing selected";
            }
            else
            {
                OutputTextBlock.Text = "Using common folder: " + CommonList.SelectedItem;
                switch (CommonList.SelectedItem.ToString().ToLower())
                {
                    case "wmi logs":
                        currentSelection = Path.Combine(Environment.SystemDirectory, "LogFiles", "WMI");
                    break;
                    case "desktop":
                        currentSelection =  Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), "Desktop");
                    break;
                    case "downloads":
                        currentSelection = Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"), "Downloads");
                    break;
                }
            }
        }
    }
}
