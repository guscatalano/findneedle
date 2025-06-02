using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using FindNeedleUX.Services;
using FindPluginCore.Searching.Serializers;
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
        PluginsFound.AddRange(MiddleLayerService.SearchQueryUX.GetLoadedPlugins().Select(plugin => new Tuple<string, object>(plugin, null)));
    }

    private readonly List<Tuple<string, object>> PluginsFound = new();

    private void ListBox2_Loaded(object sender, RoutedEventArgs e)
    {
        ListBox2.SelectedIndex = 2;
    }
}
