using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using FindNeedleUX.ViewObjects;

namespace FindNeedleUX.ViewModels;

/// <summary>
/// Testable logic for <see cref="FindNeedleUX.Pages.SearchRulesPage"/> — file
/// loading, validation, section parsing, purpose filtering, and query-state sync.
/// Has no WinUI dependencies and can be exercised without a window.
///
/// The page constructs one of these, owns it for the lifetime of the page, and
/// forwards event handlers (Browse, Remove, PurposeFilter) into its public methods.
/// The page-side <see cref="ObservableCollection{T}"/>s are these instances —
/// XAML data-binds through the page's proxy properties.
/// </summary>
public sealed class SearchRulesPageViewModel
{
    private readonly IQueryStateService _queryState;
    private bool _isLoadingFromQuery;
    private string _currentPurposeFilter = "All";

    /// <summary>
    /// All loaded rule files (valid + invalid). One entry per call to LoadRuleFile.
    /// </summary>
    public ObservableCollection<RuleFileItem> RuleFiles { get; } = new();

    /// <summary>
    /// Sections visible under the current purpose filter. A subset of
    /// (rule_file × section) reflecting <see cref="CurrentPurposeFilter"/>.
    /// </summary>
    public ObservableCollection<RuleSectionItem> RuleSections { get; } = new();

    public string CurrentPurposeFilter => _currentPurposeFilter;

    public SearchRulesPageViewModel() : this(new MiddleLayerQueryStateService()) { }

    public SearchRulesPageViewModel(IQueryStateService queryState)
    {
        _queryState = queryState;
    }

    /// <summary>
    /// Rebuilds the in-memory rule list from the current search query.
    /// Re-entrancy into <see cref="SyncRulesToQuery"/> is suppressed by an
    /// internal flag so the query isn't churned during the load.
    /// </summary>
    public void LoadRulesFromQuery()
    {
        _isLoadingFromQuery = true;
        try
        {
            RuleFiles.Clear();
            RuleSections.Clear();

            var query = _queryState.GetCurrentQuery();
            if (query?.RulesConfigPaths != null)
            {
                // Snapshot to avoid collection-modified-during-enumeration if
                // anything else mutates RulesConfigPaths mid-load.
                var paths = query.RulesConfigPaths.ToList();
                foreach (var path in paths)
                {
                    LoadRuleFile(path);
                }
            }
        }
        finally
        {
            _isLoadingFromQuery = false;
        }
    }

    /// <summary>
    /// Loads a rule file by path. Always appends exactly one <see cref="RuleFileItem"/>
    /// to <see cref="RuleFiles"/> — valid on success, invalid (with an error
    /// message) on missing/unparseable.
    /// </summary>
    public void LoadRuleFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                AddInvalidRuleFileItem(filePath, "File not found");
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

            if (root.TryGetProperty("sections", out var sectionsArray))
            {
                foreach (var section in sectionsArray.EnumerateArray())
                {
                    var sectionItem = ParseRuleSection(section, fileName);
                    if (sectionItem != null)
                    {
                        ruleFile.Sections.Add(sectionItem);
                        if (PassesPurposeFilter(sectionItem))
                            RuleSections.Add(sectionItem);
                    }
                }
            }

            RuleFiles.Add(ruleFile);
            SyncRulesToQuery();
        }
        catch (Exception ex)
        {
            AddInvalidRuleFileItem(filePath, ex.Message);
        }
    }

    /// <summary>
    /// Removes a rule file and its sections, then re-syncs the query.
    /// </summary>
    public void RemoveFile(RuleFileItem file)
    {
        foreach (var section in file.Sections.ToList())
            RuleSections.Remove(section);

        RuleFiles.Remove(file);
        SyncRulesToQuery();
    }

    /// <summary>
    /// Sets the current purpose filter ("All", "filter", "enrichment", "uml", "output")
    /// and rebuilds <see cref="RuleSections"/> from <see cref="RuleFiles"/>.
    /// </summary>
    public void SetPurposeFilter(string purpose)
    {
        _currentPurposeFilter = string.IsNullOrEmpty(purpose) ? "All" : purpose;
        RebuildVisibleSections();
    }

    // ─── internals ───────────────────────────────────────────────────────────

    /// <summary>
    /// Pushes the currently-valid rule files into the search query as the
    /// authoritative list. No-op during <see cref="LoadRulesFromQuery"/> to
    /// prevent re-entrant query churn.
    /// </summary>
    private void SyncRulesToQuery()
    {
        if (_isLoadingFromQuery)
            return;

        var query = _queryState.GetCurrentQuery();
        if (query == null) return;

        query.RulesConfigPaths.Clear();
        foreach (var file in RuleFiles.Where(f => f.IsValid))
            query.RulesConfigPaths.Add(file.FilePath);

        _queryState.NotifyStateChanged();
    }

    private void RebuildVisibleSections()
    {
        RuleSections.Clear();
        foreach (var file in RuleFiles)
            foreach (var section in file.Sections)
                if (PassesPurposeFilter(section))
                    RuleSections.Add(section);
    }

    private bool PassesPurposeFilter(RuleSectionItem section) =>
        _currentPurposeFilter == "All" || section.Purpose == _currentPurposeFilter;

    private static RuleSectionItem? ParseRuleSection(JsonElement section, string fileName)
    {
        try
        {
            var name = section.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : "";
            var description = section.TryGetProperty("description", out var descEl) ? descEl.GetString() : "";
            var purpose = section.TryGetProperty("purpose", out var purposeEl) ? purposeEl.GetString() : "";
            var ruleCount = 0;
            if (section.TryGetProperty("rules", out var rulesArray))
                ruleCount = rulesArray.GetArrayLength();

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

    private void AddInvalidRuleFileItem(string filePath, string error)
    {
        RuleFiles.Add(new RuleFileItem
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Enabled = true,
            IsValid = false,
            ValidationError = error
        });
    }
}
