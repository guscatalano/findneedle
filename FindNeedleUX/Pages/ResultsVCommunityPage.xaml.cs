using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Data;
using CommunityToolkit.WinUI.Controls;
using FindNeedleUX.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Pages;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
//[ToolkitSample(id: nameof(DataTableVirtualizationSample), "DataTable Virtualization Example", description: $"A sample for showing how to create and use a {nameof(DataTable)} control with many rows.")]
public sealed partial class ResultsVCommunityPage : Page
{
    public ResultsVCommunityPage()
    {
        List<LogLine> LogLineList = MiddleLayerService.GetLogLines();
        LogLineItems = new(LogLineList.ToArray());

        this.InitializeComponent();
    }


    public ObservableCollection<LogLine> LogLineItems
    {
        get; set;
    }

   

}
