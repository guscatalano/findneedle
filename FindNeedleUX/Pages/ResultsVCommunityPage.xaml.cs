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

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    //[ToolkitSample(id: nameof(DataTableVirtualizationSample), "DataTable Virtualization Example", description: $"A sample for showing how to create and use a {nameof(DataTable)} control with many rows.")]
    public sealed partial class ResultsVCommunityPage : Page
    {
        public ResultsVCommunityPage()
        {
            InventoryItem[] items = new InventoryItem[NumberOfRows];

            for (int i = 0; i < NumberOfRows; i++)
            {
                items[i] = new()
                {
                    Id = i,
                    Name = i.ToString(),
                    Description = i.ToString(),
                    Quantity = i,
                };
            }

            items[6].Name = "Hello, testing!";

            items[1500].Description = "This is a very long description that should have been out of view at the start...";

            InventoryItems = new(items);

            this.InitializeComponent();
        }

        public const int NumberOfRows = 10000;

        public ObservableCollection<InventoryItem> InventoryItems
        {
            get; set;
        }

       

    }

    public class InventoryItem
    {
        public int Id
        {
            get; set;
        }

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public int Quantity
        {
            get; set;
        }

        public List<InventoryItem> SubItems { get; set; } = new();
    }



}
