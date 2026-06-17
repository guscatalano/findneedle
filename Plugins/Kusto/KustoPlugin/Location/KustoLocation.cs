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

    // One credential instance per auth mode, reused across calls so the token is cached (in-memory,
    // plus an on-disk persistent cache for interactive/device) — sign in once, not on every button.
    private static readonly object _credLock = new();
    private static readonly Dictionary<KustoAuthMode, TokenCredential> _credCache = new();

    private static TokenCredential CreateCredential(KustoAuthMode authMode)
    {
        lock (_credLock)
        {
            if (_credCache.TryGetValue(authMode, out var existing)) return existing;
            var persist = new TokenCachePersistenceOptions { Name = "FindNeedleKusto" };
            TokenCredential cred = authMode switch
            {
                KustoAuthMode.AzureCli => new AzureCliCredential(),
                KustoAuthMode.DeviceCode => new DeviceCodeCredential(
                    new DeviceCodeCredentialOptions { TokenCachePersistenceOptions = persist }),
                _ => new InteractiveBrowserCredential(
                    new InteractiveBrowserCredentialOptions { TokenCachePersistenceOptions = persist }),
            };
            _credCache[authMode] = cred;
            return cred;
        }
    }

    private static KustoConnectionStringBuilder BuildConnection(string clusterUri, KustoAuthMode authMode)
    {
        var credential = CreateCredential(authMode);
        var scope = clusterUri.TrimEnd('/') + "/.default";
        // Kusto SDK calls this whenever it needs a bearer token; Azure.Identity caches internally.
        Func<string> tokenProvider = () =>
            credential.GetToken(new TokenRequestContext(new[] { scope }), CancellationToken.None).Token;
        return new KustoConnectionStringBuilder(clusterUri).WithAadTokenProviderAuthentication(tokenProvider);
    }

    private KustoConnectionStringBuilder BuildConnection() => BuildConnection(ClusterUri, AuthMode);

    /// <summary>
    /// Request options for queries: defer partial failures so a result set larger than the service
    /// cap (default 500k rows, E_QUERY_RESULT_SET_TOO_LARGE) returns the rows it got instead of
    /// throwing. The viewer shows those; refine the KQL (add a time filter / take) for the rest.
    /// </summary>
    private static ClientRequestProperties RequestProps()
    {
        var p = new ClientRequestProperties();
        p.SetOption("deferpartialqueryfailures", true);
        return p;
    }

    /// <summary>
    /// List the databases on a cluster (runs the <c>.show databases</c> management command). Used by
    /// the UI so the user can pick a database instead of typing it. Authenticates the same way a query
    /// does (so the first call may prompt sign-in). Throws on connection/auth failure.
    /// </summary>
    public static List<string> GetDatabases(string clusterUri, KustoAuthMode authMode)
    {
        var kcsb = BuildConnection((clusterUri ?? string.Empty).Trim(), authMode);
        using var admin = KustoClientFactory.CreateCslAdminProvider(kcsb);
        using var reader = admin.ExecuteControlCommand(".show databases | project DatabaseName");
        var list = new List<string>();
        while (reader.Read())
        {
            var name = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString();
            if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>List the tables in a database (<c>.show tables</c>), for the UI's table picker.</summary>
    public static List<string> GetTables(string clusterUri, string database, KustoAuthMode authMode)
    {
        var kcsb = BuildConnection((clusterUri ?? string.Empty).Trim(), authMode);
        using var admin = KustoClientFactory.CreateCslAdminProvider(kcsb);
        using var reader = admin.ExecuteControlCommand((database ?? string.Empty).Trim(), ".show tables | project TableName");
        var list = new List<string>();
        while (reader.Read())
        {
            var name = reader.IsDBNull(0) ? null : reader.GetValue(0)?.ToString();
            if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    /// <summary>
    /// Run a query and return up to <paramref name="maxRows"/> rows (columns + string cells) for a
    /// preview in the UI, plus whether more rows were available. Same auth path as a real query.
    /// </summary>
    public static (List<string> Columns, List<List<string>> Rows, bool Truncated) PreviewQuery(
        string clusterUri, string database, string query, KustoAuthMode authMode, int maxRows)
    {
        var kcsb = BuildConnection((clusterUri ?? string.Empty).Trim(), authMode);
        using var client = KustoClientFactory.CreateCslQueryProvider(kcsb);
        using var reader = client.ExecuteQuery((database ?? string.Empty).Trim(), (query ?? string.Empty).Trim(),
                                               RequestProps());

        int n = reader.FieldCount;
        var cols = new List<string>(n);
        for (int i = 0; i < n; i++) cols.Add(reader.GetName(i));

        var rows = new List<List<string>>();
        bool truncated = false;
        while (reader.Read())
        {
            if (rows.Count >= maxRows) { truncated = true; break; }
            var r = new List<string>(n);
            for (int i = 0; i < n; i++)
            {
                var v = reader.IsDBNull(i) ? null : reader.GetValue(i);
                r.Add(v?.ToString() ?? string.Empty);
            }
            rows.Add(r);
        }
        return (cols, rows, truncated);
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
            using var reader = client.ExecuteQuery(Database, Query, RequestProps());

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
