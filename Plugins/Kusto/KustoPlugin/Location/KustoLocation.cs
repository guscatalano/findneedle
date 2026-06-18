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

    /// <summary>
    /// Row cap for the query: 0 = Kusto's default (500k), a positive N raises it
    /// (<c>set truncationmaxrecords=N</c>), and -1 removes it entirely (<c>set notruncation</c>).
    /// </summary>
    public long RowLimit { get; }

    private readonly List<ISearchResult> _results = new();
    private bool _loaded;
    private string? _error;

    public KustoLocation(string clusterUri, string database, string query,
                         KustoAuthMode authMode = KustoAuthMode.Interactive, long rowLimit = 0)
    {
        ClusterUri = (clusterUri ?? string.Empty).Trim();
        Database = (database ?? string.Empty).Trim();
        Query = (query ?? string.Empty).Trim();
        AuthMode = authMode;
        RowLimit = rowLimit;
    }

    /// <summary>A leading "set" statement to raise/remove the result-row cap, or "" for default.</summary>
    private string TruncationPrefix() => RowLimit switch
    {
        < 0 => "set notruncation;\n",
        > 0 => $"set truncationmaxrecords={RowLimit};\n",
        _   => string.Empty,
    };

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

    /// <summary>Kusto's default result-set cap. Hitting exactly this implies truncation.</summary>
    private const int RowCap = 500_000;
    private bool _truncated;

    /// <summary>
    /// Query options: defer partial failures so a result set larger than the service cap returns the
    /// rows it got (up to the cap) instead of throwing — we then flag truncation by row count and
    /// warn, rather than failing outright or hiding it.
    /// </summary>
    private static ClientRequestProperties RequestProps()
    {
        var p = new ClientRequestProperties();
        p.SetOption("deferpartialqueryfailures", true);
        return p;
    }

    /// <summary>
    /// Turn a Kusto exception into a concise, actionable message (not a giant stack). For the
    /// result-set-too-large case we surface it clearly AND suggest two narrowed queries built from
    /// the user's own KQL — capping rows, and a time window — rather than silently truncating.
    /// </summary>
    public static string FriendlyError(Exception ex, string query = null)
    {
        var msg = ex?.Message ?? "Unknown error";
        bool tooLarge = msg.Contains("E_QUERY_RESULT_SET_TOO_LARGE")
                        || msg.Contains("80DA0003")
                        || msg.Contains("exceed the set limit");
        if (tooLarge)
        {
            var q = (query ?? string.Empty).TrimEnd().TrimEnd(';').TrimEnd();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("This query returns more than Kusto's 500,000-row limit, so nothing was loaded.");
            sb.AppendLine("Narrow it and try again — for example:");
            if (!string.IsNullOrWhiteSpace(q))
            {
                sb.AppendLine();
                sb.AppendLine($"{q}\n| take 100000");
                sb.AppendLine();
                sb.Append($"{q}\n| where Timestamp > ago(1h)   // replace 'Timestamp' with your time column");
            }
            else
            {
                sb.Append("Add  | take 100000   or   | where Timestamp > ago(1h)");
            }
            return sb.ToString();
        }
        return msg.Split('\n')[0].Trim();
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
        _truncated = false;
        try
        {
            var kcsb = BuildConnection();
            using var client = KustoClientFactory.CreateCslQueryProvider(kcsb);
            using var reader = client.ExecuteQuery(Database, TruncationPrefix() + Query, RequestProps());

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
            // Only the default cap truncates silently; a raised/removed cap doesn't (flag at default).
            _truncated = RowLimit == 0 && _results.Count >= RowCap;
            Logger.Instance.Log($"Kusto query returned {_results.Count} rows from {ClusterUri}/{Database}{(_truncated ? " (TRUNCATED at cap)" : "")}");
        }
        catch (Exception ex)
        {
            _error = FriendlyError(ex, Query);
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

    public override string GetDescription()
    {
        if (_error != null) return $"Kusto {ShortCluster()}/{Database} — ERROR: {_error}";
        var desc = $"Kusto cluster {ClusterUri}, database {Database}, query: {Query}";
        return _truncated
            ? $"⚠ Showing the first {RowCap:N0} rows (Kusto's cap) — refine the query (add a time filter or | take) to see the rest.  {desc}"
            : desc;
    }

    public string? LastError => _error;

    /// <summary>True after a load that hit the service row cap (results are incomplete).</summary>
    public bool WasTruncated => _truncated;

    public override void ClearStatistics() { numRecordsInLastResult = 0; }
    public override List<ReportFromComponent> ReportStatistics() => new();

    public override (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(
        CancellationToken cancellationToken = default)
        => (null, _loaded ? _results.Count : (int?)null);
}
