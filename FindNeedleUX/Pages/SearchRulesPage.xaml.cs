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
    }

    /// <summary>
    /// Public test hook: lets UI automation add a rule file by path without
    /// driving the system file picker dialog.
    /// </summary>
    public void AddRuleFileByPath(string filePath) => _viewModel.LoadRuleFile(filePath);

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        var picker = new FileOpenPicker()
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            FileTypeFilter = { ".json" }
        };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _viewModel.LoadRuleFile(file.Path);
        }
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

    private void PurposeFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PurposeFilterCombo.SelectedItem is ComboBoxItem selectedItem)
        {
            _viewModel.SetPurposeFilter(selectedItem.Tag?.ToString() ?? "All");
        }
    }
}
