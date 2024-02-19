using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using FindNeedleUX.Services;
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
using Windows.Storage.Pickers;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Windows;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class LocationAddFile : Page
{
    readonly List<string> m_FileList = new();
    public LocationAddFile()
    {
        this.InitializeComponent();
        FilesList.DataContext = m_FileList;
        WizardSelectionService.GetCurrentWizard().RegisterCurrentPage(this);
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e)
    {

        MiddleLayerService.AddFolderLocation(m_FileList.FirstOrDefault());
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
            OutputTextBlock.Text = "Picked file: " + file.DisplayName;
            m_FileList.Add(file.DisplayName);
        }
        else
        {
            OutputTextBlock.Text = "Operation cancelled.";
        }
    }
}
