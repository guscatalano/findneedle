using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FindNeedleUX.Services;
using FindPluginCore.GlobalConfiguration;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace FindNeedleUX.Pages;

public sealed partial class QuickLogWithRulesPage : Page
{
    private string _logFilePath = string.Empty;
    private string _rulesFilePath = string.Empty;
    private bool _rulesValid = false;
    private CancellationTokenSource? _cts;

    public QuickLogWithRulesPage()
    {
        this.InitializeComponent();
    }

    private async void BrowseLogButton_Click(object sender, RoutedEventArgs e)
    {
        var window = WindowUtil.GetWindowForElement(this);
        var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        var picker = new FileOpenPicker()
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            FileTypeFilter = { ".txt", ".log", ".etl", ".evtx", ".zip" }
        };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            _logFilePath = file.Path;
            LogFilePathTextBox.Text = file.Path;
            UpdateGoButtonState();
        }
    }

    private async void BrowseRulesButton_Click(object sender, RoutedEventArgs e)
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
            _rulesFilePath = file.Path;
            RulesFilePathTextBox.Text = file.Path;
            ValidateRulesFile(file.Path);
            UpdateGoButtonState();
        }
    }

    private void ValidateRulesFile(string filePath)
    {
        _rulesValid = false;
        RulesValidationPanel.Visibility = Visibility.Visible;

        try
        {
            if (!File.Exists(filePath))
            {
                ShowValidationError("File not found");
                return;
            }

            var json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check for sections array
            if (!root.TryGetProperty("sections", out var sectionsArray))
            {
                ShowValidationError("Missing 'sections' array in rules file");
                return;
            }

            var sectionCount = 0;
            var totalRuleCount = 0;
            var filterCount = 0;
            var enrichmentCount = 0;
            var umlCount = 0;

            foreach (var section in sectionsArray.EnumerateArray())
            {
                sectionCount++;

                var purpose = section.TryGetProperty("purpose", out var purposeEl) 
                    ? purposeEl.GetString() ?? "" 
                    : "";

                switch (purpose.ToLower())
                {
                    case "filter": filterCount++; break;
                    case "enrichment": enrichmentCount++; break;
                    case "uml": umlCount++; break;
                }

                if (section.TryGetProperty("rules", out var rulesArray))
                {
                    totalRuleCount += rulesArray.GetArrayLength();
                }
            }

            if (sectionCount == 0)
            {
                ShowValidationError("No sections found in rules file");
                return;
            }

            // Valid!
            _rulesValid = true;
            ShowValidationSuccess(sectionCount, totalRuleCount, filterCount, enrichmentCount, umlCount);
        }
        catch (JsonException ex)
        {
            ShowValidationError($"Invalid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            ShowValidationError($"Error reading file: {ex.Message}");
        }
    }

    private void ShowValidationSuccess(int sections, int rules, int filters, int enrichments, int umls)
    {
        ValidationIcon.Glyph = "\uE73E"; // Checkmark
        ValidationIcon.Foreground = new SolidColorBrush(Colors.Green);
        ValidationStatusText.Text = "Valid rules file";
        ValidationStatusText.Foreground = new SolidColorBrush(Colors.Green);
        RulesValidationPanel.Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(30, 0, 128, 0));

        var details = new List<string>();
        details.Add($"{sections} section(s), {rules} total rule(s)");
        
        var types = new List<string>();
        if (filters > 0) types.Add($"{filters} filter");
        if (enrichments > 0) types.Add($"{enrichments} enrichment");
        if (umls > 0) types.Add($"{umls} UML");
        
        if (types.Count > 0)
            details.Add($"Types: {string.Join(", ", types)}");

        ValidationDetailsText.Text = string.Join(" | ", details);
    }

    private void ShowValidationError(string message)
    {
        ValidationIcon.Glyph = "\uE711"; // X mark
        ValidationIcon.Foreground = new SolidColorBrush(Colors.Red);
        ValidationStatusText.Text = "Invalid rules file";
        ValidationStatusText.Foreground = new SolidColorBrush(Colors.Red);
        ValidationDetailsText.Text = message;
        RulesValidationPanel.Background = new SolidColorBrush(global::Windows.UI.Color.FromArgb(30, 128, 0, 0));
    }

    private void UpdateGoButtonState()
    {
        GoButton.IsEnabled = !string.IsNullOrEmpty(_logFilePath) && _rulesValid;
    }

    private async void GoButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_logFilePath) || string.IsNullOrEmpty(_rulesFilePath))
            return;

        // Disable UI during search
        GoButton.IsEnabled = false;
        BrowseLogButton.IsEnabled = false;
        BrowseRulesButton.IsEnabled = false;
        ProgressIndicator.IsActive = true;
        ProgressIndicator.Visibility = Visibility.Visible;
        StatusText.Text = "Setting up workspace...";

        try
        {
            _cts = new CancellationTokenSource();

            // Set up workspace with log and rules
            MiddleLayerService.NewWorkspace();
            MiddleLayerService.AddFolderLocation(_logFilePath);

            // Add rules file to query - NuSearchQuery will apply filtering from rules with purpose="filter"
            var query = MiddleLayerService.GetCurrentQuery();
            if (query != null)
            {
                query.RulesConfigPaths.Clear();
                query.RulesConfigPaths.Add(_rulesFilePath);
            }

            StatusText.Text = "Running search with rules...";

            // Run search - rules will be loaded and applied in NuSearchQuery.RunThrough()
            await Task.Run(() => MiddleLayerService.RunSearch(false, _cts.Token).Wait(), _cts.Token);

            StatusText.Text = "Search complete! Navigating to results...";

            // Navigate to results
            var viewerKey = GlobalSettings.DefaultResultViewer?.ToLower() ?? "resultswebpage";
            var viewerType = viewerKey switch
            {
                "resultsvcommunitypage" => typeof(ResultsVCommunityPage),
                "searchresultpage" => typeof(SearchResultPage),
                _ => typeof(ResultsWebPage)
            };

            if (this.Frame != null)
            {
                this.Frame.Navigate(viewerType);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Search cancelled.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            ProgressIndicator.IsActive = false;
            ProgressIndicator.Visibility = Visibility.Collapsed;
            GoButton.IsEnabled = true;
            BrowseLogButton.IsEnabled = true;
            BrowseRulesButton.IsEnabled = true;
            _cts?.Dispose();
            _cts = null;
        }
    }
}
