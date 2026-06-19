using System;
using System.Collections.ObjectModel;
using FindNeedlePluginLib;
using FindPluginCore.PluginSubsystem;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace FindNeedleUX.Pages;

/// <summary>
/// Standalone editor for the plugin DLL list (PluginConfig). Split out of the Plugins page so the
/// browse/inspect view stays focused; reached via "Configure DLLs…" there.
/// </summary>
public sealed partial class PluginConfigPage : Page
{
    public ObservableCollection<PluginConfigEntryViewModel> PluginConfigEntries { get; set; } = new();
    public PluginConfigEntryViewModel SelectedPluginConfigEntry { get; set; }

    private PluginConfig loadedConfig;
    private string loadedConfigPath = "";

    public PluginConfigPage()
    {
        this.InitializeComponent();
        Loaded += (_, _) => LoadPluginConfig();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame != null && Frame.CanGoBack) Frame.GoBack();
        else Frame?.Navigate(typeof(PluginsPage));
    }

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
            Logger.Instance.Log($"Exception in PluginConfigPage.LoadPluginConfig: {ex}");
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
            SaveStatus.Text = "Saved. Use “Reload all plugins” to apply.";
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Exception in PluginConfigPage.SavePluginConfig: {ex}");
            SaveStatus.Text = "Save failed: " + ex.Message;
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
