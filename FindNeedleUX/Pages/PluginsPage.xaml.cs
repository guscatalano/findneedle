using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
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
public sealed partial class PluginsPage : Page
{
    public PluginsPage()
    {
        this.InitializeComponent();
    }

    private List<Tuple<string, FontFamily>> PluginsFound = new List<Tuple<string, FontFamily>>()
    {
        new Tuple<string, FontFamily>("Arial", new FontFamily("Arial")),
        new Tuple<string, FontFamily>("Comic Sans MS", new FontFamily("Comic Sans MS")),
        new Tuple<string, FontFamily>("Courier New", new FontFamily("Courier New")),
        new Tuple<string, FontFamily>("Segoe UI", new FontFamily("Segoe UI")),
        new Tuple<string, FontFamily>("Times New Roman", new FontFamily("Times New Roman"))
    };

    private void ListBox2_Loaded(object sender, RoutedEventArgs e)
    {
        ListBox2.SelectedIndex = 2;
    }
}
