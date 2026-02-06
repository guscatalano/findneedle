using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using FindNeedleUX.Services;
using FindNeedleUX.ViewObjects;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FindNeedleUX.Pages;

public sealed partial class SearchRulesPage : Page
{
    public ObservableCollection<RuleFileItem> RuleFiles { get; } = new();
    public ObservableCollection<RuleSectionItem> RuleSections { get; } = new();

    private string _currentPurposeFilter = "All";

    public SearchRulesPage()
    {
        this.InitializeComponent();
        LoadRulesFromQuery();
    }

    private void LoadRulesFromQuery()
    {
        RuleFiles.Clear();
        RuleSections.Clear();

        var query = MiddleLayerService.GetCurrentQuery();
        if (query?.RulesConfigPaths != null)
        {
            foreach (var path in query.RulesConfigPaths)
            {
                LoadRuleFile(path);
            }
        }
    }

    private void LoadRuleFile(string filePath)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"LoadRuleFile called with path: {filePath}");
            
            if (!File.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"File not found: {filePath}");
                AddRuleFileItem(filePath, false, "File not found");
                return;
            }

            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var fileName = Path.GetFileName(filePath);
            var ruleFile = new RuleFileItem
            {
                FilePath = filePath,
                FileName = fileName,
                Enabled = true,
                IsValid = true
            };

            // Parse sections
            if (root.TryGetProperty("sections", out var sectionsArray))
            {
                var sectionCount = 0;
                foreach (var section in sectionsArray.EnumerateArray())
                {
                    var sectionItem = ParseRuleSection(section, fileName);
                    if (sectionItem != null)
                    {
                        ruleFile.Sections.Add(sectionItem);
                        RuleSections.Add(sectionItem);
                        sectionCount++;
                    }
                }
                System.Diagnostics.Debug.WriteLine($"Parsed {sectionCount} sections from {fileName}");
            }

            RuleFiles.Add(ruleFile);
            System.Diagnostics.Debug.WriteLine($"Added rule file: {fileName}. Total in collection: {RuleFiles.Count}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading rule file {filePath}: {ex.Message}");
            AddRuleFileItem(filePath, false, ex.Message);
        }
    }

    private RuleSectionItem? ParseRuleSection(JsonElement section, string fileName)
    {
        try
        {
            var name = section.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : "";
            var description = section.TryGetProperty("description", out var descEl) ? descEl.GetString() : "";
            var purpose = section.TryGetProperty("purpose", out var purposeEl) ? purposeEl.GetString() : "";
            var ruleCount = 0;

            if (section.TryGetProperty("rules", out var rulesArray))
            {
                ruleCount = rulesArray.GetArrayLength();
            }

            return new RuleSectionItem
            {
                Name = name ?? string.Empty,
                Description = description ?? string.Empty,
                Purpose = purpose ?? string.Empty,
                RuleCount = ruleCount,
                SourceFile = Path.GetFullPath(fileName),
                SourceFileName = fileName,
                Enabled = true
            };
        }
        catch
        {
            return null;
        }
    }

    private void AddRuleFileItem(string filePath, bool isValid, string? error = null)
    {
        RuleFiles.Add(new RuleFileItem
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Enabled = true,
            IsValid = isValid,
            ValidationError = error
        });
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;

        // Initialize file picker with window handle to prevent "invalid window" error in WinUI 3
        bool pickerInitialized = false;
        try
        {
            var app = Application.Current as App;
            if (app != null)
            {
                // Use reflection to access the private m_window field
                var windowField = typeof(App).GetField("m_window", 
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (windowField?.GetValue(app) is Microsoft.UI.Xaml.Window window && window != null)
                {
                    nint hWnd = WindowNative.GetWindowHandle(window);
                    InitializeWithWindow.Initialize(picker, hWnd);
                    pickerInitialized = true;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize picker with window handle: {ex.Message}");
        }

        if (!pickerInitialized)
        {
            System.Diagnostics.Debug.WriteLine("Warning: FileOpenPicker not initialized with window handle. This may cause UI issues.");
        }

        try
        {
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                System.Diagnostics.Debug.WriteLine($"File selected: {file.Name} at {file.Path}");
                
                if (file.Name.EndsWith(".rules.json", StringComparison.OrdinalIgnoreCase))
                {
                    LoadRuleFile(file.Path);
                    System.Diagnostics.Debug.WriteLine($"File loaded successfully. Total files: {RuleFiles.Count}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Selected file does not end with .rules.json: {file.Name}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No file was selected.");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error during file selection: {ex.Message}");
        }
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        // Placeholder - will be implemented when XAML compiles
    }

    private void RuleFilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Placeholder - will be implemented when XAML compiles
    }

    private void PurposeFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Placeholder - will be implemented when XAML compiles
    }

    private void FilterRuleSections()
    {
        RuleSections.Clear();

        foreach (var file in RuleFiles)
        {
            foreach (var section in file.Sections)
            {
                if (_currentPurposeFilter == "All" || section.Purpose == _currentPurposeFilter)
                {
                    RuleSections.Add(section);
                }
            }
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var query = MiddleLayerService.GetCurrentQuery();
        if (query != null)
        {
            query.RulesConfigPaths.Clear();
            foreach (var file in RuleFiles.Where(f => f.Enabled && f.IsValid))
            {
                query.RulesConfigPaths.Add(file.FilePath);
            }
        }

        // Navigate back or close
        if (this.Frame?.CanGoBack == true)
        {
            this.Frame.GoBack();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.Frame?.CanGoBack == true)
        {
            this.Frame.GoBack();
        }
    }
}
