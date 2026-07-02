using System;
using System.Collections.ObjectModel;
using FindNeedleUX.ViewModels;
using FindNeedleUX.ViewObjects;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace FindNeedleUX.Pages;

public sealed partial class SearchRulesPage : Page
{
    private readonly SearchRulesPageViewModel _viewModel = new();

    // XAML data-binds directly to these names — keep them as proxies onto the VM
    // so we don't need a XAML change just for the refactor.
    public ObservableCollection<RuleFileItem> RuleFiles => _viewModel.RuleFiles;
    public ObservableCollection<RuleSectionItem> RuleSections => _viewModel.RuleSections;

    public SearchRulesPage()
    {
        this.InitializeComponent();
        _viewModel.LoadRulesFromQuery();
        // The "test file path" box + Load button back an automation hook (AddRuleFileByPath); they
        // duplicate Browse and only confuse a normal user, so show them in developer mode only.
        if (!FindNeedleUX.Services.AppMode.IsDeveloper)
        {
            TestFilePathInput.Visibility = Visibility.Collapsed;
            TestLoadButton.Visibility = Visibility.Collapsed;
        }
        RuleFiles.CollectionChanged += (_, _) => UpdateRuleFilesEmptyState();
        UpdateRuleFilesEmptyState();
    }

    private void UpdateRuleFilesEmptyState()
        => EmptyRuleFilesText.Visibility = RuleFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Public test hook: lets UI automation add a rule file by path without
    /// driving the system file picker dialog.
    /// </summary>
    public void AddRuleFileByPath(string filePath) => _viewModel.LoadRuleFile(filePath);

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var path = FindNeedleUX.Services.Win32FileDialog.OpenFile(hWnd, new (string, string)[] { ("Rules JSON", "*.json") });
        if (path != null)
        {
            _viewModel.LoadRuleFile(path);
        }
    }

    /// <summary>Load a shipped example rule so a newcomer can see a working one immediately (guaranteed
    /// valid, unlike a hand-authored template). Prefers a small filter example; falls back to any shipped rule.</summary>
    private void AddSampleRule_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "CommonRules");
            var sample = System.IO.Path.Combine(dir, "crash-filter.rules.json");
            if (!System.IO.File.Exists(sample) && System.IO.Directory.Exists(dir))
            {
                sample = null;
                foreach (var f in System.IO.Directory.EnumerateFiles(dir, "*.rules.json")) { sample = f; break; }
            }
            if (!string.IsNullOrEmpty(sample) && System.IO.File.Exists(sample))
                _viewModel.LoadRuleFile(sample);
            else
                FindNeedlePluginLib.Logger.Instance.Log("Add sample rule: no CommonRules examples found next to the app.");
        }
        catch (Exception ex) { FindNeedlePluginLib.Logger.Instance.Log($"Add sample rule failed: {ex.Message}"); }
    }

    private void TestFilePathInput_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Enter)
            LoadFileFromTestHook();
    }

    private void TestLoadButton_Click(object sender, RoutedEventArgs e) => LoadFileFromTestHook();

    private void LoadFileFromTestHook()
    {
        if (!string.IsNullOrWhiteSpace(TestFilePathInput.Text))
        {
            _viewModel.LoadRuleFile(TestFilePathInput.Text.Trim());
            TestFilePathInput.Text = string.Empty;
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (RuleFilesListBox.SelectedItem is RuleFileItem selectedFile)
        {
            _viewModel.RemoveFile(selectedFile);
            RemoveButton.IsEnabled = false;
        }
    }

    private void RuleFilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RemoveButton.IsEnabled = RuleFilesListBox.SelectedItem != null;
    }

    private void OpenRuleFile_Click(object sender, RoutedEventArgs e)
    {
        var path = (sender as Button)?.Tag as string;
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (!System.IO.File.Exists(path))
            {
                FindNeedlePluginLib.Logger.Instance.Log($"Open rule file: not found: {path}");
                return;
            }
            // Open in the OS default handler for .json (editor). UseShellExecute is required to resolve
            // the file association from a packaged/unpackaged WinUI app.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { FindNeedlePluginLib.Logger.Instance.Log($"Open rule file failed for {path}: {ex.Message}"); }
    }

    private void PurposeFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PurposeFilterCombo.SelectedItem is ComboBoxItem selectedItem)
        {
            _viewModel.SetPurposeFilter(selectedItem.Tag?.ToString() ?? "All");
        }
    }
}
