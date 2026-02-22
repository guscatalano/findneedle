using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using findneedle;
using FindNeedlePluginLib;
using FindPluginCore; // Add for Logger
using findneedle.PluginSubsystem;
using FindPluginCore.Implementations.Storage;
using FindNeedlePluginLib.Interfaces; // Fix missing ISearchStorage reference
using FindPluginCore.PluginSubsystem; // For StorageType and PluginConfig
using findneedle.RuleDSL; // For RuleLoader and RuleEvaluationEngine
using FindNeedleRuleDSL; // For OutputRuleProcessor

namespace FindPluginCore.Searching;
public class NuSearchQuery : ISearchQuery
{
    public List<ISearchFilter> Filters
    {
        get => [];
        set
        {

        }
    }
    private readonly List<ISearchFilter> _filters;

    public List<IResultProcessor> Processors
    {
        get => _processors;
        set => _processors = value;
    }
    private List<IResultProcessor> _processors;

    public List<ISearchOutput> Outputs
    {
        get => [];
        set
        {

        }
    }
    private readonly List<ISearchOutput> _outputs;

    public List<ISearchLocation> Locations
    {
        get => _locations;
        set => _locations = value;
    }
    private List<ISearchLocation> _locations;

    public SearchLocationDepth Depth
    {
        get => _depth;
        set => _depth = value;
    }
    private SearchLocationDepth _depth;

    public SearchStatistics Statistics
    {
        get => _stats;
        set { /* can't set readonly field, but for interface compliance, do nothing or throw if needed */ }
    }
    public SearchStatistics stats
    {
        get => _stats;
        set { /* can't set readonly field, but for interface compliance, do nothing or throw if needed */ }
    }
    private readonly SearchStatistics _stats;

    public List<string> RulesConfigPaths { get; set; } = new();
    public object? LoadedRules { get; set; }

    // Rule processing components
    private readonly RuleLoader _ruleLoader = new();
    private readonly RuleEvaluationEngine _ruleEngine = new();
    private readonly OutputRuleProcessor _outputProcessor = new();

    public SearchStepNotificationSink SearchStepNotificationSink
    {
        get => _stepnotifysink;
        set => _stepnotifysink = value;
    }
    private SearchStepNotificationSink _stepnotifysink;

    public List<ISearchResult> CurrentResultList => _currentResultList;

    public string Name
    {
        get => "noname";
        set
        {
            // Optionally implement setter logic or leave empty for interface compliance
        }
    }

    private List<ISearchResult> _currentResultList;

    private ISearchStorage? _resultStorage; // Use ISearchStorage instead of InMemoryStorage

    public NuSearchQuery()
    {
        _filters = new();
        _outputs = new();
        _processors = new();
        _depth = SearchLocationDepth.Shallow;
        _locations = new();
        _currentResultList = new();
        _stats = new();
        _stepnotifysink = new();
        _stats.RegisterForNotifications(_stepnotifysink, this);
        _stepnotifysink.NotifyStep(SearchStep.AtLaunch);
        Logger.Instance.Log("NuSearchQuery constructed");
        _resultStorage = null;
    }

    // Remove duplicate declaration of filePath in CreateStorage
    private ISearchStorage CreateStorage(CancellationToken cancellationToken)
    {
        var config = PluginManager.GetSingleton().config;
        var filePath = _locations.Count > 0 ? _locations[0].GetName() : "default";
        switch (config?.SearchStorageType)
        {
            case StorageType.SqlLite:
                return new SqliteStorage(filePath);
            case StorageType.InMemory:
                return new InMemoryStorage();
            case StorageType.Auto:
            default:
                var totalRecords = 0;
                TimeSpan totalTime = TimeSpan.Zero;
                foreach (var loc in _locations)
                {
                    try
                    {
                        var perf = loc.GetSearchPerformanceEstimate(cancellationToken);
                        if (perf.recordCount.HasValue)
                            totalRecords += perf.recordCount.Value;
                        if (perf.timeTaken.HasValue)
                            totalTime += perf.timeTaken.Value;
                    }
                    catch (NotImplementedException)
                    {
                        totalRecords += 100;
                    }
                }
                // Heuristic: Use InMemory if < 10,000 records and estimated time < 30s, else SqlLite
                if (totalRecords < 10_000 && totalTime.TotalSeconds < 30)
                    return new InMemoryStorage();
                else
                    return new SqliteStorage(filePath);
        }
    }

    public void RunThrough()
    {
        RunThrough(CancellationToken.None);
    }

    public void RunThrough(CancellationToken cancellationToken)
    {
        Logger.Instance.Log("RunThrough (with cancellation) started");
        LoadRules(); // Load rules before processing
        Step1_LoadAllLocationsInMemory(cancellationToken);
        _currentResultList = Step2_GetFilteredResults(cancellationToken);
        Step3_ResultsToProcessors();
        Step4_ProcessAllResultsToOutput();
        Step5_Done();
        Logger.Instance.Log("RunThrough (with cancellation) finished");
    }

    /// <summary>
    /// Load rules from configured paths.
    /// </summary>
    private void LoadRules()
    {
        if (RulesConfigPaths == null || RulesConfigPaths.Count == 0)
        {
            Logger.Instance.Log("No rules config paths specified");
            return;
        }

        try
        {
            LoadedRules = _ruleLoader.LoadRulesFromPaths(RulesConfigPaths);
            Logger.Instance.Log($"Loaded rules from {RulesConfigPaths.Count} paths");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Error loading rules: {ex.Message}");
        }
    }

    #region main functions
    public void Step1_LoadAllLocationsInMemory()
    {
        Step1_LoadAllLocationsInMemory(CancellationToken.None);
    }

    public void Step1_LoadAllLocationsInMemory(CancellationToken cancellationToken)
    {
        Logger.Instance.Log($"Step1_LoadAllLocationsInMemory (with cancellation): {_locations.Count} locations");
        var count = 1;
        var total = _locations.Count;
        _ = PluginManager.GetSingleton();
        foreach (var loc in _locations)
        {
            if (cancellationToken.IsCancellationRequested) return;
            Logger.Instance.Log($"Loading location {count}/{total}: {loc.GetName()}");
            if (loc is FindNeedlePluginLib.Interfaces.IReportProgress reportable)
            {
                reportable.SetProgressSink(_stepnotifysink.progressSink);
            }
            var percent = total > 0 ? (int)(50.0 * count / total) : 0;
            _stepnotifysink.progressSink.NotifyProgress(percent, "loading location: " + loc.GetName());
            loc.LoadInMemory(cancellationToken);
            Logger.Instance.Log($"Loaded location: {loc.GetName()}");
            try
            {
                var perf = loc.GetSearchPerformanceEstimate(cancellationToken);
                Logger.Instance.Log($"Performance estimate for {loc.GetName()}: time={perf.timeTaken}, records={perf.recordCount}");
            }
            catch (NotImplementedException)
            {
                Logger.Instance.Log($"Performance estimate not implemented for {loc.GetName()}");
            }
            count++;
        }
        _stepnotifysink.NotifyStep(SearchStep.AtLoad);
        Logger.Instance.Log("Step1_LoadAllLocationsInMemory (with cancellation) complete");
    }

    private List<ISearchResult>? _filteredResults;
    public List<ISearchResult> Step2_GetFilteredResults()
    {
        return Step2_GetFilteredResults(CancellationToken.None);
    }

    public List<ISearchResult> Step2_GetFilteredResults(CancellationToken cancellationToken)
    {
        Logger.Instance.Log("Step2_GetFilteredResults (with cancellation) started");
        _stepnotifysink.NotifyStep(SearchStep.AtSearch);
        _filteredResults = new();
        _resultStorage = CreateStorage(cancellationToken); // Now called at the start of step 2
        var storageType = _resultStorage is InMemoryStorage ? "InMemoryStorage" : _resultStorage is SqliteStorage ? "SqliteStorage" : _resultStorage.GetType().Name;
        var count = 1;
        var total = _locations.Count;
        var pluginManager = PluginManager.GetSingleton();
        var useSync = pluginManager.config?.UseSynchronousSearch ?? false;
        foreach (var loc in _locations)
        {
            if (cancellationToken.IsCancellationRequested) break;
            Logger.Instance.Log($"Filtering results for location {count}/{total}: {loc.GetName()}");
            var percent = total > 0 ? 50 + (int)(50.0 * count / total) : 50;
            _stepnotifysink.progressSink.NotifyProgress(percent, "loading results: " + loc.GetName() + $" using storage: {storageType}" );
            loc.SetSearchDepth(_depth);
            List<ISearchResult> rawResults = new();
            if (!useSync)
            {
                try
                {
                    loc.SearchWithCallback(batch => {
                        rawResults.AddRange(batch);
                        Logger.Instance.Log($"SearchWithCallback for {loc.GetName()} returned batch of {batch.Count} raw results");
                        _resultStorage.AddRawBatch(batch, cancellationToken);
                    }, cancellationToken).Wait();
                }
                catch (NotImplementedException)
                {
                    Logger.Instance.Log($"SearchWithCallback not implemented for {loc.GetName()}");
                }
            }
            else
            {
                try
                {
                    rawResults = loc.Search(cancellationToken);
                    Logger.Instance.Log($"Search for {loc.GetName()} returned {rawResults.Count} raw results");
                    _resultStorage.AddRawBatch(rawResults, cancellationToken);
                }
                catch (NotImplementedException)
                {
                    Logger.Instance.Log($"Search not implemented for {loc.GetName()}");
                }
            }
            // Filter and add to filtered batch
            var filteredBatch = new List<ISearchResult>();
            foreach (var result in rawResults)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var passAllFilters = true;
                foreach (var filter in _filters)
                {
                    if (!filter.Filter(result))
                    {
                        passAllFilters = false;
                    }
                }
                if (passAllFilters)
                {
                    filteredBatch.Add(result);
                }
            }
            _resultStorage.AddFilteredBatch(filteredBatch, cancellationToken);
            Logger.Instance.Log($"Results stored for location: {loc.GetName()}");
            count++;
        }
        // Gather all filtered results from storage
        var allResults = new List<ISearchResult>();
        _resultStorage.GetFilteredResultsInBatches(batch => allResults.AddRange(batch), 1000, cancellationToken);
        
        // Apply rule-based filtering if rules are loaded
        if (LoadedRules != null)
        {
            Logger.Instance.Log("Applying rule-based filtering...");
            allResults = ApplyRuleFiltering(allResults);
            Logger.Instance.Log($"After rule filtering: {allResults.Count} results");
        }
        
        _filteredResults = allResults;
        Logger.Instance.Log($"Step2_GetFilteredResults (with cancellation) complete: {_filteredResults.Count} total filtered results");
        return _filteredResults;
    }

    /// <summary>
    /// Apply rule-based filtering from loaded rules with purpose="filter".
    /// </summary>
    private List<ISearchResult> ApplyRuleFiltering(List<ISearchResult> results)
    {
        try
        {
            var filterSections = _ruleLoader.GetSectionsByPurpose(LoadedRules, "filter");
            if (filterSections == null || !filterSections.Any())
            {
                Logger.Instance.Log("No filter sections found in rules");
                return results;
            }

            Logger.Instance.Log($"Found {filterSections.Count()} filter section(s)");
            var filtered = new List<ISearchResult>();
            
            foreach (var result in results)
            {
                var include = false; // Default to exclude unless a rule matches with include action
                var explicitlyExcluded = false;

                foreach (var section in filterSections)
                {
                    var evalResult = _ruleEngine.EvaluateRules(result, section);
                    
                    // If any rule matched with include action, include the result
                    if (evalResult.Include)
                    {
                        include = true;
                    }
                    
                    // Check if explicitly excluded
                    if (!evalResult.Include && evalResult.Tags.Count == 0)
                    {
                        // Rule matched but set Include to false = explicit exclude
                        explicitlyExcluded = true;
                    }
                }

                if (include && !explicitlyExcluded)
                {
                    filtered.Add(result);
                }
            }

            Logger.Instance.Log($"Rule filtering: {results.Count} -> {filtered.Count} results");
            return filtered;
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Error applying rule filtering: {ex.Message}");
            return results;
        }
    }

    public void Step3_ResultsToProcessors()
    {
        Logger.Instance.Log("Step3_ResultsToProcessors started");
        _stepnotifysink.NotifyStep(SearchStep.AtProcessor);
        
        // Apply enrichment rules if loaded
        if (LoadedRules != null)
        {
            ApplyRuleEnrichment();
        }
        
        // Run any configured processors
        foreach (var proc in _processors)
        {
            Logger.Instance.Log($"Processing results with processor: {proc.GetType().Name}");
            proc.ProcessResults(_currentResultList);
            Logger.Instance.Log($"Processor {proc.GetType().Name} complete");
        }
        
        Logger.Instance.Log("Step3_ResultsToProcessors complete");
    }

    /// <summary>
    /// Apply rule-based enrichment from loaded rules with purpose="enrichment".
    /// </summary>
    private void ApplyRuleEnrichment()
    {
        try
        {
            var enrichmentSections = _ruleLoader.GetSectionsByPurpose(LoadedRules, "enrichment");
            if (enrichmentSections == null || !enrichmentSections.Any())
            {
                Logger.Instance.Log("No enrichment sections found in rules");
                return;
            }

            Logger.Instance.Log($"Found {enrichmentSections.Count()} enrichment section(s)");
            var totalTags = 0;

            foreach (var result in _currentResultList)
            {
                foreach (var section in enrichmentSections)
                {
                    var evalResult = _ruleEngine.EvaluateRules(result, section);
                    
                    // Apply tags to the result
                    foreach (var tag in evalResult.Tags)
                    {
                        // Store tags in extended properties if the result supports it
                        // For now, just log the tags
                        totalTags++;
                    }
                }
            }

            Logger.Instance.Log($"Rule enrichment: applied {totalTags} tags to {_currentResultList.Count} results");
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Error applying rule enrichment: {ex.Message}");
        }
    }

    public void Step4_ProcessAllResultsToOutput()
    {
        Logger.Instance.Log("Step4_ProcessAllResultsToOutput started");
        _stepnotifysink.NotifyStep(SearchStep.AtOutput);
        
        // Apply output rules if loaded
        if (LoadedRules != null)
        {
            ApplyRuleOutput();
        }
        
        // Run standard outputs
        foreach (var output in _outputs)
        {
            Logger.Instance.Log($"Writing all output with: {output.GetType().Name}");
            output.WriteAllOutput(_currentResultList);
        }
        Logger.Instance.Log("Step4_ProcessAllResultsToOutput complete");
    }

    /// <summary>
    /// Apply rule-based output from loaded rules with purpose="output".
    /// </summary>
    private void ApplyRuleOutput()
    {
        try
        {
            var outputSections = _ruleLoader.GetSectionsByPurpose(LoadedRules, "output");
            if (outputSections == null || !outputSections.Any())
            {
                Logger.Instance.Log("No output sections found in rules");
                return;
            }

            Logger.Instance.Log($"Found {outputSections.Count()} output section(s)");
            _outputProcessor.ProcessOutputRules(_currentResultList, outputSections);
        }
        catch (Exception ex)
        {
            Logger.Instance.Log($"Error applying rule output: {ex.Message}");
        }
    }

    public void Step5_Done()
    {
        Logger.Instance.Log("Step5_Done called");
        _stepnotifysink.NotifyStep(SearchStep.Total);
    }
    #endregion

    public void SetDepthForAllLocations(SearchLocationDepth depthForAllLocations)
    {
        foreach (var loc in _locations)
        {
            loc.SetSearchDepth(depthForAllLocations);
        }
    }

    //Consider rethinking this one
    public void AddFilter(ISearchFilter filter) 
    {
        _filters.Add(filter);
    }
    public List<ISearchFilter> GetFilters()
    {
        return _filters;
    }
    public SearchStatistics GetSearchStatistics()
    {
        return _stats;
    }
    public List<ISearchLocation> GetLocations()
    {
        return _locations;
    }
}
