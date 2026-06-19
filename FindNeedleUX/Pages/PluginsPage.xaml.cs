using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using FindPluginCore.PluginSubsystem;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using FindNeedlePluginLib;
using WinRT.Interop;

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
}

public class ModuleViewModel
{
    public string ModulePath { get; set; }
    public ObservableCollection<PluginListItemViewModel> Plugins { get; set; } = new();
    public bool LoadedSuccessfully { get; set; }
    public Exception LoadException { get; set; }
    public string LoadExceptionString { get; set; }
    public int PluginCount => Plugins.Count;
}

/// <summary>One row in the modules→plugins tree (a module header or a plugin under it).</summary>
public class PluginTreeNode
{
    public string Glyph { get; set; }
    public Brush GlyphBrush { get; set; }
    public string Text { get; set; }
    public ModuleViewModel Module { get; set; }              // set for module rows
    public PluginListItemViewModel Plugin { get; set; }      // set for plugin rows
}

public sealed partial class PluginsPage : Page
{
    public ObservableCollection<PluginConfigEntryViewModel> PluginConfigEntries { get; set; } = new();
    public PluginConfigEntryViewModel SelectedPluginConfigEntry { get; set; }
    public ObservableCollection<ModuleViewModel> ModulesFound { get; set; } = new();

    private PluginConfig loadedConfig;
    private string loadedConfigPath = "";

    private static Brush Ok => (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
    private static Brush Bad => (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
    private static Brush Warn => (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];

    public PluginsPage()
    {
        this.InitializeComponent();
        ShowInitProgress(true);
        SetBodyEnabled(false);
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await Task.Run(() => MiddleLayerService.SearchQueryUX.Initialize());
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
                SetBodyEnabled(true);
                try
                {
                    PopulateModulesFromManager(findneedle.PluginSubsystem.PluginManager.GetSingleton());
                    LoadPluginConfig();
                }
                catch (Exception ex)
                {
                    Logger.Instance.Log($"Exception populating PluginsPage: {ex}");
                }
            });
        }
    }

    private void ShowInitProgress(bool show)
    {
        if (InitProgressBar != null)
            InitProgressBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    // Grid is a Panel (no IsEnabled) — dim + block input while plugins load.
    private void SetBodyEnabled(bool enabled)
    {
        if (BodyGrid == null) return;
        BodyGrid.IsHitTestVisible = enabled;
        BodyGrid.Opacity = enabled ? 1.0 : 0.5;
    }

    // ----- master: build the modules → plugins tree -----
    private void PopulateModulesFromManager(findneedle.PluginSubsystem.PluginManager manager)
    {
        ModulesFound.Clear();
        foreach (var module in manager.loadedPluginsModules)
        {
            var modulePath = module.dll != null ? module.dll.Location : "Unknown";
            var moduleVM = new ModuleViewModel
            {
                ModulePath = modulePath,
                LoadedSuccessfully = module.LoadedSuccessfully,
                LoadException = module.LoadException,
                LoadExceptionString = module.LoadExceptionString,
            };
            foreach (var plugin in module.description)
            {
                moduleVM.Plugins.Add(new PluginListItemViewModel
                {
                    Name = plugin.FriendlyName,
                    Description = plugin.TextDescription,
                    ModulePath = modulePath,
                    ClassName = plugin.ClassName,
                    Plugin = plugin,
                    ImplementedInterfaces = plugin.ImplementedInterfacesShort,
                });
            }
            if (moduleVM.Plugins.Count > 0)
                ModulesFound.Add(moduleVM);
        }
        BuildTree();
    }

    private void BuildTree()
    {
        PluginTree.RootNodes.Clear();
        PluginListItemViewModel first = null;
        foreach (var module in ModulesFound)
        {
            var moduleName = System.IO.Path.GetFileName(module.ModulePath);
            var modNode = new TreeViewNode
            {
                IsExpanded = true,
                Content = new PluginTreeNode
                {
                    Module = module,
                    Glyph = module.LoadedSuccessfully ? "✓" : "✗",
                    GlyphBrush = module.LoadedSuccessfully ? Ok : Bad,
                    Text = $"{moduleName} ({module.PluginCount})",
                },
            };
            foreach (var plugin in module.Plugins)
            {
                bool valid = plugin.Plugin.validPlugin;
                modNode.Children.Add(new TreeViewNode
                {
                    Content = new PluginTreeNode
                    {
                        Plugin = plugin,
                        Module = module,
                        Glyph = valid ? "✓" : "⚠",
                        GlyphBrush = valid ? Ok : Warn,
                        Text = plugin.Name,
                    },
                });
                first ??= plugin;
            }
            PluginTree.RootNodes.Add(modNode);
        }
        // Seed the detail panel with the first plugin so the page isn't blank.
        if (first != null) ShowPlugin(first);
        else ClearDetail();
    }

    private void PluginTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        var data = (args.InvokedItem as TreeViewNode)?.Content as PluginTreeNode
                   ?? args.InvokedItem as PluginTreeNode;
        if (data == null) return;
        if (data.Plugin != null) ShowPlugin(data.Plugin);
        else if (data.Module != null) ShowModuleOnly(data.Module);
    }

    // ----- detail -----
    private void ShowPlugin(PluginListItemViewModel plugin)
    {
        DetailTitle.Text = plugin.Name;
        bool valid = plugin.Plugin.validPlugin;
        PluginDescriptionTextBlock.Text = valid
            ? (plugin.Description ?? string.Empty)
            : "This plugin does not implement IPluginDescription.";
        PluginClassNameTextBlock.Text = plugin.ClassName ?? string.Empty;
        PluginInterfacesTextBlock.Text = plugin.ImplementedInterfacesDisplay;

        var module = FindModuleFor(plugin);
        ShowModuleStatus(module);
    }

    private void ShowModuleOnly(ModuleViewModel module)
    {
        DetailTitle.Text = System.IO.Path.GetFileName(module.ModulePath);
        PluginDescriptionTextBlock.Text = $"{module.PluginCount} plugin(s) in this module. Select one to see details.";
        PluginClassNameTextBlock.Text = string.Empty;
        PluginInterfacesTextBlock.Text = string.Empty;
        ShowModuleStatus(module);
    }

    private void ShowModuleStatus(ModuleViewModel module)
    {
        if (module == null)
        {
            PluginModuleTextBlock.Text = string.Empty;
            ModuleLoadedStatusTextBlock.Text = string.Empty;
            ModuleLoadExceptionTextBlock.Text = string.Empty;
            ModuleLoadExceptionStringTextBlock.Text = string.Empty;
            return;
        }
        PluginModuleTextBlock.Text = module.ModulePath ?? string.Empty;
        ModuleLoadedStatusTextBlock.Text = module.LoadedSuccessfully ? "Loaded successfully" : "Failed to load";
        // Show error details only when there actually was a load failure.
        ModuleLoadExceptionTextBlock.Text = module.LoadException?.Message ?? string.Empty;
        ModuleLoadExceptionStringTextBlock.Text = string.IsNullOrEmpty(module.LoadExceptionString) ? string.Empty : module.LoadExceptionString;
    }

    private void ClearDetail()
    {
        DetailTitle.Text = "No plugins loaded";
        PluginDescriptionTextBlock.Text = string.Empty;
        PluginClassNameTextBlock.Text = string.Empty;
        PluginInterfacesTextBlock.Text = string.Empty;
        ShowModuleStatus(null);
    }

    private ModuleViewModel FindModuleFor(PluginListItemViewModel plugin)
    {
        foreach (var m in ModulesFound)
            if (m.Plugins.Contains(plugin))
                return m;
        return null;
    }

    // ----- plugin config -----
    private void LoadPluginConfig()
    {
        try
        {
            var manager = findneedle.PluginSubsystem.PluginManager.GetSingleton();
            loadedConfig = manager.config;
            loadedConfigPath = typeof(findneedle.PluginSubsystem.PluginManager)
                .GetField("loadedConfig", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(manager)?.ToString() ?? "PluginConfig.json";
            PluginConfigEntries.Clear();
            if (loadedConfig != null)
                foreach (var entry in loadedConfig.entries)
                    PluginConfigEntries.Add(new PluginConfigEntryViewModel { Name = entry.name, Path = entry.path, Enabled = entry.enabled });
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Exception in LoadPluginConfig: {ex}");
        }
    }

    private void AddPluginConfigEntry_Click(object sender, RoutedEventArgs e)
        => PluginConfigEntries.Add(new PluginConfigEntryViewModel { Name = "", Path = "", Enabled = true });

    private void RemovePluginConfigEntry_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPluginConfigEntry != null)
        {
            PluginConfigEntries.Remove(SelectedPluginConfigEntry);
            SelectedPluginConfigEntry = null;
        }
    }

    private void SavePluginConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (loadedConfig == null) return;
            loadedConfig.entries.Clear();
            foreach (var vm in PluginConfigEntries)
                loadedConfig.entries.Add(new PluginConfigEntry { name = vm.Name, path = vm.Path, enabled = vm.Enabled });
            findneedle.PluginSubsystem.PluginManager.GetSingleton().SaveToFile(loadedConfigPath);
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Exception in SavePluginConfig_Click: {ex}");
        }
    }

    private void ReloadAllPlugins_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var manager = findneedle.PluginSubsystem.PluginManager.GetSingleton();
            manager.loadedPluginsModules.Clear();
            manager.LoadAllPlugins();
            PopulateModulesFromManager(manager);
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Exception in ReloadAllPlugins_Click: {ex}");
        }
    }

    private void PickPluginFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PluginConfigEntryViewModel entry }) return;
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WindowNative.GetWindowHandle(window);
        var path = FindNeedleUX.Services.Win32FileDialog.OpenFile(hWnd, new (string, string)[] { ("Plugin DLL", "*.dll") });
        if (path != null) entry.Path = path;
    }
}
