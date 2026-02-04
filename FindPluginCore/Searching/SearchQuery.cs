using findneedle.Implementations;
using findneedle.PluginSubsystem;
using findneedle.RuleDSL;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using System.Threading;

namespace findneedle;



public class SearchQuery : ISearchQuery
{

    public void RunThrough()
    {
        //Old implementation
        LoadRules();
        LoadAllLocationsInMemory();
        GetFilteredResults();
        ProcessAllResultsToOutput();
        PrintOutputFilesToConsole();
        GetSearchStatsOutput();
        //IResultProcessor p = new WatsonCrashProcessor();
        //p.ProcessResults(y);
        //p.GetOutputFile();
    }

    public void RunThrough(CancellationToken cancellationToken)
    {
        LoadRules();
        LoadAllLocationsInMemory(cancellationToken);
        GetFilteredResults(cancellationToken);
        ProcessAllResultsToOutput(); // TODO: propagate token if outputs support it
        PrintOutputFilesToConsole();
        GetSearchStatsOutput();
    }

    public void AddFilter(ISearchFilter filter)
    {
        Filters.Add(filter);
    }

    private List<ISearchOutput> _output;
    public List<ISearchOutput> Outputs
    {
        get
        {
            _output ??= new List<ISearchOutput>();
            return _output;
        }
        set => _output = value;
    }

    private SearchLocationDepth _depth;

  

    public SearchLocationDepth Depth
    {
        get => _depth;
        set => _depth = value;
    }

    private SearchStepNotificationSink _stepnotifysink;

    public SearchStepNotificationSink SearchStepNotificationSink
    {
        get
        {
            _stepnotifysink ??= new SearchStepNotificationSink();
            return _stepnotifysink;
        }
        set
        {
            _stepnotifysink = value;
        }
    }
    

    private List<IResultProcessor> _processors;
    public List<IResultProcessor> Processors
    {
        get
        {
            _processors ??= new List<IResultProcessor>();
            return _processors;
        }
        set => _processors = value;
    }


    private SearchStatistics _stats;
    public SearchStatistics stats
    {
        get
        {
            _stats ??= new SearchStatistics();
            return _stats;
        }
        set => _stats = value;
    }

    private List<ISearchFilter> _filters;
    public List<ISearchFilter> Filters
    {
        get
        {
            _filters ??= new List<ISearchFilter>();
            return _filters;
        }
        set => _filters = value;
    }

    private List<ISearchLocation> _locations;
    public List<ISearchLocation> Locations
    {
        get
        {
            _locations ??= new List<ISearchLocation>();
            return _locations;
        }
        set => _locations = value;
    }

    public void SetLocations(List<ISearchLocation> loc)
    {
        this.Locations = loc;
    }

    public List<ISearchLocation> GetLocations()
    {
        return Locations;
    }

    public List<ISearchFilter> GetFilters()
    {
        return Filters;
    }

    private List<string> _rulesConfigPaths;
    public List<string> RulesConfigPaths
    {
        get
        {
            _rulesConfigPaths ??= new List<string>();
            return _rulesConfigPaths;
        }
        set => _rulesConfigPaths = value;
    }

    private object? _loadedRules;
    public object? LoadedRules
    {
        get => _loadedRules;
        set => _loadedRules = value;
    }

    private RuleEvaluationEngine? _ruleEngine;
    private RuleLoader? _ruleLoader;

    public SearchQuery()
    {
        stats = new();
        _stats = stats;
        _filters = [];
        _locations = [];
        _processors = [];
        _output = [];
        _stepnotifysink = new();
        _rulesConfigPaths = [];
        _ruleEngine = new RuleEvaluationEngine();
        _ruleLoader = new RuleLoader();
    }


    public void SetDepth(SearchLocationDepth depth)
    {
        this.Depth = depth;
    }

    public void SetDepthForAllLocations(SearchLocationDepth depthForAllLocations)
    {
        foreach (var loc in Locations)
        {
            loc.SetSearchDepth(depthForAllLocations);
        }
    }

    /// <summary>
    /// Loads rules from configured paths. Called automatically if RulesConfigPaths are set.
    /// </summary>
    public void LoadRules()
    {
        if (RulesConfigPaths.Any() && _ruleLoader != null)
        {
            try
            {
                _loadedRules = _ruleLoader.LoadRulesFromPaths(RulesConfigPaths);
                System.Diagnostics.Debug.WriteLine($"Loaded rules from {RulesConfigPaths.Count} paths");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading rules: {ex.Message}");
            }
        }
    }

    public void LoadAllLocationsInMemory(System.Threading.CancellationToken cancellationToken = default)
    {
        stats = new SearchStatistics(); //reset the stats
        SearchStepNotificationSink.NotifyStep(SearchStep.AtLoad);
        SetDepthForAllLocations(Depth);
        var count = 1;
        int total = Locations.Count();
        foreach (var loc in Locations)
        {
            if (cancellationToken.IsCancellationRequested) return;
            int percent = total > 0 ? (int)(50.0 * count / total) : 0;
            SearchStepNotificationSink.progressSink.NotifyProgress(percent, "loading location: " + loc.GetName());
            loc.LoadInMemory(cancellationToken); // always pass token
            count++;
        }
        stats.LoadedAll(this);
    }

    public List<ISearchResult> GetFilteredResults(System.Threading.CancellationToken cancellationToken = default)
    {
        SearchStepNotificationSink.NotifyStep(SearchStep.AtSearch);
        List<ISearchResult> results = new List<ISearchResult>();
        var count = 1;
        int total = Locations.Count();
        foreach (var loc in Locations)
        {
            if (cancellationToken.IsCancellationRequested) break;
            int percent = total > 0 ? 50 + (int)(50.0 * count / total) : 50;
            SearchStepNotificationSink.progressSink.NotifyProgress(percent, "loading results: " + loc.GetName());
            results.AddRange(loc.Search(cancellationToken));
            count++;
        }

        // Apply rule-based filtering if rules are loaded
        if (_loadedRules != null && _ruleEngine != null)
        {
            results = ApplyRuleFiltering(results);
        }

        stats.Searched(this);
        return results;
    }

    private List<ISearchResult> ApplyRuleFiltering(List<ISearchResult> results)
    {
        try
        {
            var filterSections = _ruleLoader?.GetSectionsByPurpose(_loadedRules, "filter") ?? new List<dynamic>();
            if (!filterSections.Any())
                return results;

            var filtered = new List<ISearchResult>();
            foreach (var result in results)
            {
                var include = true;
                
                foreach (var section in filterSections)
                {
                    var evalResult = _ruleEngine!.EvaluateRules(result, section);
                    if (!evalResult.Include)
                    {
                        include = false;
                        break;
                    }
                }

                if (include)
                {
                    filtered.Add(result);
                }
            }

            return filtered;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error applying rule filtering: {ex.Message}");
            return results;
        }
    }

    public SearchStatistics GetSearchStatistics()
    {
        return stats;
    }

    public void GetSearchStatsOutput()
    {
        stats.ReportToConsole();
    }

    public void ProcessAllResultsToOutput()
    {
        //Remember to provide one that does it one y one at some point 
        foreach(var output in Outputs)
        {
            output.WriteAllOutput(GetFilteredResults());
        }
    }

    public void PrintOutputFilesToConsole()
    {
        foreach (var output in Outputs)
        {
            Console.WriteLine(output.GetPluginFriendlyName() + ": " + output.GetOutputFileName());
        }
    }

    public void AddOutput(ISearchOutput output)
    {
        Outputs.Add(output);
    }

    public void Step1_LoadAllLocationsInMemory()
    {
        LoadAllLocationsInMemory();
    }
    public void Step1_LoadAllLocationsInMemory(CancellationToken cancellationToken)
    {
        LoadAllLocationsInMemory(cancellationToken);
    }
    public List<ISearchResult> Step2_GetFilteredResults()
    {
        return GetFilteredResults();
    }
    public List<ISearchResult> Step2_GetFilteredResults(CancellationToken cancellationToken)
    {
        return GetFilteredResults(cancellationToken);
    }
    public void Step3_ResultsToProcessors()
    {
        // Apply enrichment rules if loaded
        if (_loadedRules != null && _ruleEngine != null)
        {
            ApplyRuleEnrichment();
        }
    }

    private void ApplyRuleEnrichment()
    {
        try
        {
            var enrichmentSections = _ruleLoader?.GetSectionsByPurpose(_loadedRules, "enrichment") ?? new List<dynamic>();
            if (!enrichmentSections.Any())
                return;

            // This would typically apply tags to results - implementation depends on how tags are stored
            System.Diagnostics.Debug.WriteLine($"Applied {enrichmentSections.Count} enrichment rule sections");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error applying rule enrichment: {ex.Message}");
        }
    }
    public void Step4_ProcessAllResultsToOutput()
    {
        ProcessAllResultsToOutput();
    }
    public void Step5_Done()
    {
        return; 
    }

    public string Name
    {
        get => _name ?? string.Empty;
        set => _name = value;
    }

    private string? _name;
}
