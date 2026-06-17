using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using FindNeedlePluginLib;
using Kusto.Data;
using Kusto.Data.Common;
using Kusto.Data.Net.Client;

namespace KustoPlugin.Location;

/// <summary>How FindNeedle authenticates to the Kusto/ADX cluster.</summary>
public enum KustoAuthMode
{
    /// <summary>Pop an Azure AD (Entra) browser sign-in on first query. Simplest for desktop.</summary>
    Interactive,
    /// <summary>Reuse an existing <c>az login</c> session (Azure CLI must be installed + logged in).</summary>
    AzureCli,
    /// <summary>Print a code + URL to enter on another device.</summary>
    DeviceCode,
}

/// <summary>
/// A live Kusto (Azure Data Explorer) cluster as a search location: runs a KQL query against a
/// cluster + database and maps the result rows into <see cref="ISearchResult"/> for the viewer.
/// Authentication is via Azure.Identity (interactive / Azure CLI / device code) fed to the Kusto
/// SDK as a bearer-token provider.
/// </summary>
public class KustoLocation : ISearchLocation
{
    public string ClusterUri { get; }
    public string Database { get; }
    public string Query { get; }
    public KustoAuthMode AuthMode { get; }

    private readonly List<ISearchResult> _results = new();
    private bool _loaded;
    private string? _error;

    public KustoLocation(string clusterUri, string database, string query,
                         KustoAuthMode authMode = KustoAuthMode.Interactive)
    {
        ClusterUri = (clusterUri ?? string.Empty).Trim();
        Database = (database ?? string.Empty).Trim();
        Query = (query ?? string.Empty).Trim();
        AuthMode = authMode;
    }

    private TokenCredential CreateCredential() => AuthMode switch
    {
        KustoAuthMode.AzureCli => new AzureCliCredential(),
        KustoAuthMode.DeviceCode => new DeviceCodeCredential(),
        _ => new InteractiveBrowserCredential(),
    };

    private KustoConnectionStringBuilder BuildConnection()
    {
        var credential = CreateCredential();
        var scope = ClusterUri.TrimEnd('/') + "/.default";
        // Kusto SDK calls this whenever it needs a bearer token; Azure.Identity caches internally.
        Func<string> tokenProvider = () =>
            credential.GetToken(new TokenRequestContext(new[] { scope }), CancellationToken.None).Token;
        return new KustoConnectionStringBuilder(ClusterUri).WithAadTokenProviderAuthentication(tokenProvider);
    }

    public override void LoadInMemory(CancellationToken cancellationToken = default)
    {
        if (_loaded) return;
        _results.Clear();
        _error = null;
        try
        {
            var kcsb = BuildConnection();
            using var client = KustoClientFactory.CreateCslQueryProvider(kcsb);
            using var reader = client.ExecuteQuery(Database, Query, new ClientRequestProperties());

            int cols = reader.FieldCount;
            var names = new string[cols];
            for (int i = 0; i < cols; i++) names[i] = reader.GetName(i);

            var label = $"{ShortCluster()}/{Database}";
            while (reader.Read())
            {
                if (cancellationToken.IsCancellationRequested) break;
                var map = new Dictionary<string, string>(cols, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < cols; i++)
                {
                    var v = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    map[names[i]] = v?.ToString() ?? string.Empty;
                }
                _results.Add(new KustoRowResult(map, label));
            }
            Logger.Instance.Log($"Kusto query returned {_results.Count} rows from {ClusterUri}/{Database}");
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            Logger.Instance.Log($"Kusto query failed for {ClusterUri}/{Database}: {ex}");
        }
        _loaded = true;
        numRecordsInLastResult = _results.Count;
        numRecordsInMemory = _results.Count;
    }

    public override List<ISearchResult> Search(CancellationToken cancellationToken = default)
    {
        LoadInMemory(cancellationToken);
        return _results;
    }

    public override async Task SearchWithCallback(Action<List<ISearchResult>> onBatch,
                                                  CancellationToken cancellationToken = default,
                                                  int batchSize = 1000)
    {
        LoadInMemory(cancellationToken);
        var batch = new List<ISearchResult>(batchSize);
        foreach (var r in _results)
        {
            if (cancellationToken.IsCancellationRequested) break;
            batch.Add(r);
            if (batch.Count >= batchSize) { onBatch(batch); batch = new List<ISearchResult>(batchSize); }
        }
        if (batch.Count > 0) onBatch(batch);
        await Task.CompletedTask;
    }

    private string ShortCluster()
    {
        try { return new Uri(ClusterUri).Host.Split('.')[0]; } catch { return ClusterUri; }
    }

    public override string GetName() => $"Kusto: {ShortCluster()}/{Database}";

    public override string GetDescription() =>
        _error != null
            ? $"Kusto {ShortCluster()}/{Database} — ERROR: {_error}"
            : $"Kusto cluster {ClusterUri}, database {Database}, query: {Query}";

    public string? LastError => _error;

    public override void ClearStatistics() { numRecordsInLastResult = 0; }
    public override List<ReportFromComponent> ReportStatistics() => new();

    public override (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(
        CancellationToken cancellationToken = default)
        => (null, _loaded ? _results.Count : (int?)null);
}
