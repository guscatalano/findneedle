using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using FindNeedleUX.Services;
using FindPluginCore.PluginSubsystem;
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
using FindNeedlePluginLib.Interfaces;

namespace FindNeedleUX.Pages;

public class PluginConfigEntryViewModel
{
    public string Name { get; set; }
    public string Path { get; set; }
    public bool Enabled { get; set; }
}

public class PluginListItemViewModel
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string ModulePath { get; set; }
    public PluginDescription Plugin { get; set; }
}

public class ModuleViewModel
{
    public string ModulePath { get; set; }
    public ObservableCollection<PluginListItemViewModel> Plugins { get; set; } = new();
}

public sealed partial class PluginsPage : Page
{
    public ObservableCollection<PluginConfigEntryViewModel> PluginConfigEntries { get; set; } = new();
    public PluginConfigEntryViewModel SelectedPluginConfigEntry { get; set; }
    public ObservableCollection<ModuleViewModel> ModulesFound { get; set; } = new();
    public ModuleViewModel SelectedModule { get; set; }
    public ObservableCollection<PluginListItemViewModel> PluginsInSelectedModule { get; set; } = new();
    public PluginListItemViewModel SelectedPlugin { get; set; }
    private PluginConfig? loadedConfig;
    private string loadedConfigPath = "";

    public PluginsPage()
    {
        this.InitializeComponent();

        // Ensure plugins are loaded before populating UI
        MiddleLayerService.SearchQueryUX.Initialize();

        try
        {
            var manager = findneedle.PluginSubsystem.PluginManager.GetSingleton();
            var allModules = manager.loadedPluginsModules;
            foreach (var module in allModules)
            {
                string modulePath = "Unknown";
                try
                {
                    if (module.dll != null)
                        modulePath = module.dll.Location;
                    var moduleVM = new ModuleViewModel { ModulePath = modulePath };
                    foreach (var plugin in module.description)
                    {
                        try
                        {
                            moduleVM.Plugins.Add(new PluginListItemViewModel
                            {
                                Name = plugin.FriendlyName,
                                Description = plugin.TextDescription,
                                ModulePath = modulePath,
                                Plugin = plugin
                            });
                        }
                        catch (Exception ex)
                        {
                            FindPluginCore.Logger.Instance.Log($"Exception loading plugin in PluginsPage constructor: {ex}");
                        }
                    }
                    if (moduleVM.Plugins.Count > 0)
                        ModulesFound.Add(moduleVM);
                }
                catch (Exception ex)
                {
                    FindPluginCore.Logger.Instance.Log($"Exception loading module in PluginsPage constructor: {ex}");
                }
            }
            if (ModulesFound.Count > 0)
            {
                SelectedModule = ModulesFound[0];
                UpdatePluginsInSelectedModule();
            }
            LoadPluginConfig();
            UpdatePluginDescription();
            ModuleSelectorComboBox.SelectionChanged += ModuleSelectorComboBox_SelectionChanged;
            PluginSelectorComboBox.SelectionChanged += PluginSelectorComboBox_SelectionChanged;
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in PluginsPage constructor: {ex}");
        }
    }

    private void UpdatePluginsInSelectedModule()
    {
        try
        {
            PluginsInSelectedModule.Clear();
            if (SelectedModule != null)
            {
                foreach (var plugin in SelectedModule.Plugins)
                    PluginsInSelectedModule.Add(plugin);
                if (PluginsInSelectedModule.Count > 0)
                    SelectedPlugin = PluginsInSelectedModule[0];
                else
                    SelectedPlugin = null;
            }
            else
            {
                SelectedPlugin = null;
            }
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in UpdatePluginsInSelectedModule: {ex}");
        }
    }

    private void ModuleSelectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            UpdatePluginsInSelectedModule();
            UpdatePluginDescription();
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in ModuleSelectorComboBox_SelectionChanged: {ex}");
        }
    }

    private void PluginSelectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            UpdatePluginDescription();
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in PluginSelectorComboBox_SelectionChanged: {ex}");
        }
    }

    private void UpdatePluginDescription()
    {
        try
        {
            if (SelectedPlugin != null && PluginDescriptionTextBlock != null)
            {
                PluginDescriptionTextBlock.Text = SelectedPlugin.Description ?? string.Empty;
                if (PluginModuleTextBlock != null)
                    PluginModuleTextBlock.Text = SelectedPlugin.ModulePath ?? string.Empty;
            }
            else
            {
                if (PluginDescriptionTextBlock != null) PluginDescriptionTextBlock.Text = string.Empty;
                if (PluginModuleTextBlock != null) PluginModuleTextBlock.Text = string.Empty;
            }
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in UpdatePluginDescription: {ex}");
        }
    }

    private void LoadPluginConfig()
    {
        try
        {
            var manager = findneedle.PluginSubsystem.PluginManager.GetSingleton();
            loadedConfig = manager.config;
            loadedConfigPath = typeof(findneedle.PluginSubsystem.PluginManager).GetField("loadedConfig", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(manager)?.ToString() ?? "PluginConfig.json";
            PluginConfigEntries.Clear();
            if (loadedConfig != null)
            {
                foreach (var entry in loadedConfig.entries)
                {
                    PluginConfigEntries.Add(new PluginConfigEntryViewModel { Name = entry.name, Path = entry.path, Enabled = entry.enabled });
                }
            }
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in LoadPluginConfig: {ex}");
        }
    }

    private void AddPluginConfigEntry_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PluginConfigEntries.Add(new PluginConfigEntryViewModel { Name = "", Path = "", Enabled = true });
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in AddPluginConfigEntry_Click: {ex}");
        }
    }

    private void RemovePluginConfigEntry_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SelectedPluginConfigEntry != null)
            {
                PluginConfigEntries.Remove(SelectedPluginConfigEntry);
                SelectedPluginConfigEntry = null;
            }
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in RemovePluginConfigEntry_Click: {ex}");
        }
    }

    private void SavePluginConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (loadedConfig == null) return;
            loadedConfig.entries.Clear();
            foreach (var vm in PluginConfigEntries)
            {
                loadedConfig.entries.Add(new PluginConfigEntry { name = vm.Name, path = vm.Path, enabled = vm.Enabled });
            }
            try
            {
                var manager = findneedle.PluginSubsystem.PluginManager.GetSingleton();
                manager.SaveToFile(loadedConfigPath);
            }
            catch (Exception ex)
            {
                FindPluginCore.Logger.Instance.Log($"Exception in SavePluginConfig_Click (SaveToFile): {ex}");
            }
        }
        catch (Exception ex)
        {
            FindPluginCore.Logger.Instance.Log($"Exception in SavePluginConfig_Click: {ex}");
        }
    }
}
