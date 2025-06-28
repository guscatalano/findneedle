using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using findneedle.PluginSubsystem;
using FindNeedlePluginLib.Interfaces;
using findneedle.Interfaces;
using System.Collections.ObjectModel;
using FindPluginCore.PluginSubsystem;
using FindNeedleUX.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FindNeedleUX.Pages;
/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class SearchProcessorsPage : Page
{
    public class ProcessorDisplayItem
    {
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; }
        public string ConfigKey { get; set; } // Used to update config
    }

    public ObservableCollection<ProcessorDisplayItem> Processors { get; set; } = new();

    private static bool pluginsLoaded = false;

    public SearchProcessorsPage()
    {
        this.InitializeComponent();
        this.Loaded += SearchProcessorsPage_Loaded;
    }

    private void SearchProcessorsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var pluginManager = PluginManager.GetSingleton();
        // Only load plugins if not already loaded in this session
        if (!pluginsLoaded && (pluginManager.loadedPluginsModules == null || pluginManager.loadedPluginsModules.Count == 0))
        {
            pluginManager.LoadAllPlugins();
            pluginsLoaded = true;
        }
        LoadProcessors();
        ProcessorsListView.ItemsSource = Processors;
    }

    private void LoadProcessors()
    {
        Processors.Clear();
        var pluginManager = PluginManager.GetSingleton();
        var config = pluginManager.config;
        var enabledDict = new Dictionary<string, bool>();
        if (config != null)
        {
            foreach (var entry in config.entries)
            {
                // Use ClassName or Name as key
                var key = entry.name;
                enabledDict[key] = entry.enabled;
            }
        }
        foreach (var module in pluginManager.loadedPluginsModules)
        {
            foreach (var desc in module.description)
            {
                if (desc.ImplementedInterfacesShort.Contains("IResultProcessor"))
                {
                    var name = !string.IsNullOrEmpty(desc.FriendlyName) ? desc.FriendlyName : desc.ClassName;
                    var configKey = desc.FriendlyName ?? desc.ClassName;
                    bool enabled;
                    if (enabledDict.TryGetValue(desc.FriendlyName ?? desc.ClassName, out var isEnabled))
                        enabled = isEnabled;
                    else if (enabledDict.TryGetValue(desc.ClassName, out isEnabled))
                        enabled = isEnabled;
                    else if (enabledDict.TryGetValue(desc.SourceFile, out isEnabled))
                        enabled = isEnabled;
                    else
                        enabled = true; // default to true if not found
                    Processors.Add(new ProcessorDisplayItem { Name = name, Enabled = enabled, ConfigKey = configKey });
                }
            }
        }
    }

    private void CheckBox_Checked(object sender, RoutedEventArgs e)
    {
        UpdateProcessorEnabledState(sender, true);
    }

    private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        UpdateProcessorEnabledState(sender, false);
    }

    private void UpdateProcessorEnabledState(object sender, bool enabled)
    {
        if (sender is CheckBox cb && cb.DataContext is ProcessorDisplayItem item && item.ConfigKey != null)
        {
            var pluginManager = PluginManager.GetSingleton();
            var config = pluginManager.config;
            if (config != null)
            {
                var entry = config.entries.FirstOrDefault(e => e.name == item.ConfigKey);
                if (entry != null)
                {
                    entry.enabled = enabled;
                    item.Enabled = enabled;
                    pluginManager.SaveToFile();
                }
            }
        }
        // Always refresh the search query so processor list is up to date
        MiddleLayerService.UpdateSearchQuery();
    }
}
