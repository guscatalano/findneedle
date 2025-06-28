using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using FindNeedlePluginLib;
using FindNeedleUX.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Documents;
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
public sealed partial class SearchStatisticsPage : Page
{
    public SearchStatisticsPage()
    {
        this.InitializeComponent();
    }
    private void TabView_Loaded(object sender, RoutedEventArgs e)
    {
        for (var i = 0; i < 3; i++)
        {
            (sender as TabView).TabItems.Add(CreateNewTab(i));
        }
    }

    private void TabView_AddButtonClick(TabView sender, object args)
    {
        sender.TabItems.Add(CreateNewTab(sender.TabItems.Count));
    }

    private void TabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        sender.TabItems.Remove(args.Tab);
    }

    private TabViewItem CreateNewTab(int index)
    {
        TabViewItem newItem = new TabViewItem();

        newItem.Header = $"Document {index}";
        newItem.IconSource = new Microsoft.UI.Xaml.Controls.SymbolIconSource() { Symbol = Symbol.Document };

        // The content of the tab is often a frame that contains a page, though it could be any UIElement.
        Frame frame = new Frame();
        frame.Height = 500;
        StackPanel x = new() { Height = 500 };
        TextBox y = new() { AcceptsReturn = true, Text = "ohono" };
        var tet = string.Empty;
        ScrollViewer scrollViewer = new()
        {
            Height = 500,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            VerticalScrollMode = ScrollMode.Enabled
        };
        TreeView treeeee = new() { AllowDrop = false };
        TreeViewNode parent = new();
        treeeee.RootNodes.Add(parent);

        var middlestats = MiddleLayerService.GetStats();


        if (!middlestats.componentReports.ContainsKey(SearchStep.AtLoad))
        {
            return newItem;
        }
        var z = middlestats.componentReports[SearchStep.AtLoad];
        switch (index % 3)
        {
            case 0:
                foreach (var i in z)
                {
                    TreeViewNode node = new();
                    parent.Children.Add(node);
                    node.Content = i.summary + "-" + i.component;
                    tet += Environment.NewLine + i.summary + "-" + i.component;// + i.metric
                    foreach (var j in i.metric)
                    {

                        TreeViewNode node2 = new();
                        node.Children.Add(node2);
                        node.Content = i.summary + "-" + i.component;
                        if (i.summary.Equals("ExtensionProviders"))
                        {
                            //tet += Environment.NewLine + j.Key + "==> " + j.Value;
                            node2.Content = j.Key + "==> " + j.Value;
                        }
                        else if (i.summary.Equals("ProviderByFile"))
                        {
                            node2.Content = j.Key;
                            foreach (KeyValuePair<string, int> jj in j.Value)
                            {
                                TreeViewNode node3 = new();
                                node2.Children.Add(node3);
                                node3.Content = jj.Key + " --> " + jj.Value;
                            }
                        }
                        else
                        {

                            node2.Content = j.Key;
                            foreach (KeyValuePair<string, string> jj in j.Value)
                            {
                                TreeViewNode node3 = new();
                                node2.Children.Add(node3);
                                node3.Content = jj.Key + " --> " + jj.Value;
                            }
                        }
                    }
                }
                y.Text = tet;

                break;
            case 1:
                // frame.Navigate(typeof(SamplePage2));
                break;
            case 2:
                // frame.Navigate(typeof(SamplePage3));
                break;
        }


        //x.Children.Add(y);
        scrollViewer.Content = treeeee;
        x.Children.Add(scrollViewer);
        newItem.Content = x;

        //newItem.Content = frame;

        return newItem;
    }
}
