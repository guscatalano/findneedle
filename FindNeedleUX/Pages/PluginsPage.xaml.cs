using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
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
using FindNeedlePluginLib;
using Windows.Storage.Pickers;
using Windows.Storage;

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
    public string ClassName { get; set; }
    public PluginDescription Plugin { get; set; }
    public List<string> ImplementedInterfaces { get; set; }
    public string ImplementedInterfacesDisplay => ImplementedInterfaces != null && ImplementedInterfaces.Count > 0 ? string.Join(", ", ImplementedInterfaces) : "(none)";
    public bool IsDummy { get; set; } = false;
}

public class ModuleViewModel
{
    public string ModulePath { get; set; }
    public ObservableCollection<PluginListItemViewModel> Plugins { get; set; } = new();
    public bool LoadedSuccessfully { get; set; }
    public Exception LoadException { get; set; }
    public string LoadExceptionString { get; set; }
    public int PluginCount => Plugins.Count;
    public string DisplayName => $"{ModulePath} ({PluginCount} plugins)";
}

public sealed partial class PluginsPage : Page
{
    public ObservableCollection<PluginConfigEntryViewModel> PluginConfigEntries { get; set; } = new();
    public PluginConfigEntryViewModel SelectedPluginConfigEntry { get; set; }
    public ObservableCollection<ModuleViewModel> ModulesFound { get; set; } = new();
    public ModuleViewModel SelectedModule { get; set; }
    public ObservableCollection<PluginListItemViewModel> PluginsInSelectedModule { get; set; } = new();
    public PluginListItemViewModel SelectedPlugin { get; set; }
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
    private PluginConfig? loadedConfig;
#pragma warning restore CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
    private string loadedConfigPath = "";
    private bool hideInvalidPlugins = true;

    public PluginsPage()
    {
        this.InitializeComponent();
        ShowInitProgress(true);
        SetMainContentEnabled(false);
        // Run plugin initialization async so UI can update
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await Task.Run(() =>
            {
                // Ensure plugins are loaded before populating UI
                MiddleLayerService.SearchQueryUX.Initialize();
            });
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Exception in PluginsPage InitializeAsync: {ex}");
        }
        finally
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ShowInitProgress(false);
                SetMainContentEnabled(true);
                // Now run the rest of the constructor logic
                try
                {
                    var manager = findneedle.PluginSubsystem.PluginManager.GetSingleton();
                    var allModules = manager.loadedPluginsModules;
                    ModulesFound.Clear();
                    foreach (var module in allModules)
                    {
                        var modulePath = "Unknown";
                        try
                        {
                            if (module.dll != null)
                                modulePath = module.dll.Location;
                            var moduleVM = new ModuleViewModel { ModulePath = modulePath, LoadedSuccessfully = module.LoadedSuccessfully, LoadException = module.LoadException, LoadExceptionString = module.LoadExceptionString };
                            foreach (var plugin in module.description)
                            {
                                try
                                {
                                    moduleVM.Plugins.Add(new PluginListItemViewModel
                                    {
                                        Name = plugin.FriendlyName,
                                        Description = plugin.TextDescription,
                                        ModulePath = modulePath,
                                        ClassName = plugin.ClassName,
                                        Plugin = plugin,
                                        ImplementedInterfaces = plugin.ImplementedInterfacesShort
                                    });
                                }
                                catch (Exception ex)
                                {
                                    Logger.Instance.Log($"Exception loading plugin in PluginsPage constructor: {ex}");
                                }
                            }
                            if (moduleVM.Plugins.Count > 0)
                                ModulesFound.Add(moduleVM);
                        }
                        catch (Exception ex)
                        {
                            Logger.Instance.Log($"Exception loading module in PluginsPage constructor: {ex}");
                        }
                    }
                    if (ModulesFound.Count > 0)
                    {
                        SelectedModule = ModulesFound[0];
                        if (ModuleSelectorComboBox != null)
                            ModuleSelectorComboBox.SelectedItem = SelectedModule;
                        UpdatePluginsInSelectedModule();
                    }
                    LoadPluginConfig();
                    UpdatePluginDescription();
                    ModuleSelectorComboBox.SelectionChanged += ModuleSelectorComboBox_SelectionChanged;
                    PluginSelectorComboBox.SelectionChanged += PluginSelectorComboBox_SelectionChanged;
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"Exception in PluginsPage constructor: {ex}");
                }
            });
        }
    }

    private void ShowInitProgress(bool show)
    {
        if (InitProgressBar != null)
            InitProgressBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetMainContentEnabled(bool enabled)
    {
        if (MainContentPanel != null)
        {
            foreach (var child in MainContentPanel.Children)
            {
                if (child is Control ctrl)
                    ctrl.IsEnabled = enabled;
            }
        }
    }

    private void UpdatePluginsInSelectedModule()
    {
        try
        {
            PluginsInSelectedModule.Clear();
            if (SelectedModule != null)
            {
                var plugins = hideInvalidPlugins
                    ? SelectedModule.Plugins.Where(p => p.Plugin.validPlugin)
                    : SelectedModule.Plugins;
                var any = false;
                foreach (var plugin in plugins)
                {
                    PluginsInSelectedModule.Add(plugin);
                    any = true;
                }
                if (!any)
                {
                    PluginsInSelectedModule.Add(new PluginListItemViewModel
                    {
                        Name = "No valid plugins found",
                        Description = string.Empty,
                        ModulePath = SelectedModule.ModulePath,
                        ClassName = string.Empty,
                        Plugin = default,
                        ImplementedInterfaces = new List<string>(),
                        IsDummy = true
                    });
                }
                // Always select the first plugin if available
                if (PluginsInSelectedModule.Count > 0)
                {
                    SelectedPlugin = PluginsInSelectedModule[0];
                    if (PluginSelectorComboBox != null)
                        PluginSelectorComboBox.SelectedItem = SelectedPlugin;
                }
                else
                {
                    SelectedPlugin = null;
                    if (PluginSelectorComboBox != null)
                        PluginSelectorComboBox.SelectedItem = null;
                }
            }
            else
            {
                SelectedPlugin = null;
                if (PluginSelectorComboBox != null)
                    PluginSelectorComboBox.SelectedItem = null;
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Exception in UpdatePluginsInSelectedModule: {ex}");
        }
    }

    private void HideInvalidPluginsCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        hideInvalidPlugins = true;
        UpdatePluginsInSelectedModule();
    }

    private void HideInvalidPluginsCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        hideInvalidPlugins = false;
        UpdatePluginsInSelectedModule();
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
            Logger.Instance.Log($"Exception in ModuleSelectorComboBox_SelectionChanged: {ex}");
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
            Logger.Instance.Log($"Exception in PluginSelectorComboBox_SelectionChanged: {ex}");
        }
    }

    private void UpdatePluginDescription()
    {
        try
        {
            // Update plugin description/module info
            if (SelectedPlugin != null && PluginDescriptionTextBlock != null)
            {
                if (!SelectedPlugin.IsDummy && !SelectedPlugin.Plugin.validPlugin)
                {
                    PluginDescriptionTextBlock.Text = "This plugin needs to implement IPluginDescription";
                }
                else
                {
                    PluginDescriptionTextBlock.Text = SelectedPlugin.Description ?? string.Empty;
                }
                if (PluginModuleTextBlock != null)
                    PluginModuleTextBlock.Text = SelectedPlugin.ModulePath ?? string.Empty;
                if (PluginClassNameTextBlock != null)
                    PluginClassNameTextBlock.Text = SelectedPlugin.ClassName ?? string.Empty;
                if (PluginInterfacesTextBlock != null)
                    PluginInterfacesTextBlock.Text = SelectedPlugin.ImplementedInterfacesDisplay;
            }
            else
            {
                if (PluginDescriptionTextBlock != null) PluginDescriptionTextBlock.Text = string.Empty;
                if (PluginModuleTextBlock != null) PluginModuleTextBlock.Text = string.Empty;
                if (PluginClassNameTextBlock != null) PluginClassNameTextBlock.Text = string.Empty;
                if (PluginInterfacesTextBlock != null) PluginInterfacesTextBlock.Text = string.Empty;
            }
            // Update module status info
            if (SelectedModule != null)
            {
                if (ModuleLoadedStatusTextBlock != null)
                    ModuleLoadedStatusTextBlock.Text = $"LoadedSuccessfully: {SelectedModule.LoadedSuccessfully}";
                if (ModuleLoadExceptionTextBlock != null)
                    ModuleLoadExceptionTextBlock.Text = $"LoadException: {SelectedModule.LoadException?.ToString() ?? "(none)"}";
                if (ModuleLoadExceptionStringTextBlock != null)
                    ModuleLoadExceptionStringTextBlock.Text = $"LoadExceptionString: {SelectedModule.LoadExceptionString ?? "(none)"}";
            }
            else
            {
                if (ModuleLoadedStatusTextBlock != null) ModuleLoadedStatusTextBlock.Text = string.Empty;
                if (ModuleLoadExceptionTextBlock != null) ModuleLoadExceptionTextBlock.Text = string.Empty;
                if (ModuleLoadExceptionStringTextBlock != null) ModuleLoadExceptionStringTextBlock.Text = string.Empty;
            }
            // Disable plugin ComboBox if only dummy entry
            if (PluginSelectorComboBox != null)
            {
                PluginSelectorComboBox.IsEnabled = !(PluginsInSelectedModule.Count == 1 && PluginsInSelectedModule[0].IsDummy);
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Exception in UpdatePluginDescription: {ex}");
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
            Logger.Instance.Log($"Exception in LoadPluginConfig: {ex}");
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
            Logger.Instance.Log($"Exception in AddPluginConfigEntry_Click: {ex}");
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
            Logger.Instance.Log($"Exception in RemovePluginConfigEntry_Click: {ex}");
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
                Logger.Instance.Log($"Exception in SavePluginConfig_Click (SaveToFile): {ex}");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Exception in SavePluginConfig_Click: {ex}");
        }
    }

    private void ReloadPlugin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (SelectedModule != null)
            {
                // Example: reload the module (actual implementation may vary)
                // You may want to call PluginManager to reload the module by path
                var manager = findneedle.PluginSubsystem.PluginManager.GetSingleton();
                // This is a placeholder for actual reload logic
                // manager.ReloadModule(SelectedModule.ModulePath);
                Logger.Instance.Log($"Reload requested for module: {SelectedModule.ModulePath}");
                // Optionally, refresh the UI after reload
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Exception in ReloadPlugin_Click: {ex}");
        }
    }

    private async void ReloadAllPlugins_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var manager = findneedle.PluginSubsystem.PluginManager.GetSingleton();
            manager.loadedPluginsModules.Clear();
            manager.LoadAllPlugins();
            // Refresh UI
            ModulesFound.Clear();
            foreach (var module in manager.loadedPluginsModules)
            {
                var modulePath = module.dll != null ? module.dll.Location : "Unknown";
                var moduleVM = new ModuleViewModel { ModulePath = modulePath, LoadedSuccessfully = module.LoadedSuccessfully, LoadException = module.LoadException ?? null!, LoadExceptionString = module.LoadExceptionString };
                foreach (var plugin in module.description)
                {
                    moduleVM.Plugins.Add(new PluginListItemViewModel
                    {
                        Name = plugin.FriendlyName,
                        Description = plugin.TextDescription,
                        ModulePath = modulePath,
                        ClassName = plugin.ClassName,
                        Plugin = plugin,
                        ImplementedInterfaces = plugin.ImplementedInterfacesShort
                    });
                }
                if (moduleVM.Plugins.Count > 0)
                    ModulesFound.Add(moduleVM);
            }
            if (ModulesFound.Count > 0)
            {
                SelectedModule = ModulesFound[0];
                if (ModuleSelectorComboBox != null)
                    ModuleSelectorComboBox.SelectedItem = SelectedModule;
                UpdatePluginsInSelectedModule();
            }
            UpdatePluginDescription();
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Exception in ReloadAllPlugins_Click: {ex}");
        }
    }

    private async void PickPluginFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PluginConfigEntryViewModel entry)
        {
            var picker = new FileOpenPicker();
            // Use the current window for WinUI 3
            var window = Microsoft.UI.Xaml.Window.Current;
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);
            picker.FileTypeFilter.Add(".dll");
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                entry.Path = file.Path;
            }
        }
    }
}
