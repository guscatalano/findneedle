using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Azure.Core;
using Azure.Identity;
using findneedle.Implementations;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;

namespace ADOPlugin.Location;

/// <summary>How FindNeedle authenticates to Azure DevOps.</summary>
public enum AdoAuthMode
{
    /// <summary>Personal Access Token (Basic auth). Works for any org with no AAD app setup.</summary>
    Pat,
    /// <summary>Azure AD browser sign-in via Azure.Identity (org must allow AAD).</summary>
    Interactive,
}

/// <summary>
/// An Azure DevOps project as a search location: pulls work items (via a WIQL query or an explicit
/// list of IDs) over the REST API and maps each into an <see cref="AdoWorkItemResult"/>. Works with
/// any org — both <c>https://dev.azure.com/org</c> and the legacy <c>https://org.visualstudio.com</c>.
/// </summary>
public class AdoLocation : ISearchLocation
{
    // The Azure DevOps resource id used as the AAD token scope for interactive auth.
    private const string AdoResourceScope = "499b84ac-1321-427f-aa17-267ca6975798/.default";

    public string OrganizationUrl { get; }
    public string Project { get; }
    public AdoAuthMode AuthMode { get; }
    public string Pat { get; }
    /// <summary>Explicit work item ids (comma/space separated). When set, the query is ignored.</summary>
    public string Ids { get; }
    /// <summary>WIQL query. Empty = recently-changed default.</summary>
    public string Wiql { get; }
    public int Top { get; }
    /// <summary>When true, download + parse the work items' file attachments instead of listing them.</summary>
    public bool OpenAttachments { get; }

    private const string ApiVersion = "7.0";
    private const string DefaultWiql =
        "SELECT [System.Id] FROM WorkItems ORDER BY [System.ChangedDate] DESC";

    private readonly List<ISearchResult> _results = new();
    private bool _loaded;
    private string? _error;

    public AdoLocation(string organizationUrl, string project, AdoAuthMode authMode,
                       string pat = "", string wiql = "", string ids = "", int top = 200,
                       bool openAttachments = false)
    {
        OrganizationUrl = (organizationUrl ?? "").Trim().TrimEnd('/');
        Project = (project ?? "").Trim();
        AuthMode = authMode;
        Pat = pat ?? "";
        Wiql = (wiql ?? "").Trim();
        Ids = (ids ?? "").Trim();
        Top = top <= 0 ? 200 : top;
        OpenAttachments = openAttachments;
    }

    // ----- HTTP / auth -----

    private static readonly object _credLock = new();
    private static InteractiveBrowserCredential? _cred;

    /// <summary>Force the interactive AAD sign-in now (so the browser prompt happens when the user adds
    /// the location, not later during the first search). Caches the token; safe to call repeatedly.
    /// Throws if sign-in fails/cancels.</summary>
    public static void PrimeInteractiveCredential() => GetAadToken();

    private static string GetAadToken()
    {
        lock (_credLock)
        {
            _cred ??= new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions
            {
                TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = "FindNeedleAdo" },
            });
        }
        return _cred.GetToken(new TokenRequestContext(new[] { AdoResourceScope }), CancellationToken.None).Token;
    }

    private HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        if (AuthMode == AdoAuthMode.Pat)
        {
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + Pat));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }
        else
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GetAadToken());
        }
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    /// <summary>Resolve the work item ids to fetch — explicit IDs if given, else run the WIQL query.</summary>
    private List<int> ResolveIds(HttpClient http, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(Ids))
        {
            return Ids.Split(new[] { ',', ' ', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => int.TryParse(s.Trim(), out var n) ? n : -1)
                      .Where(n => n > 0).Distinct().ToList();
        }

        var projSeg = string.IsNullOrEmpty(Project) ? "" : Uri.EscapeDataString(Project) + "/";
        var url = $"{OrganizationUrl}/{projSeg}_apis/wit/wiql?api-version={ApiVersion}";
        var body = JsonSerializer.Serialize(new { query = string.IsNullOrEmpty(Wiql) ? DefaultWiql : Wiql });
        using var resp = http.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct)
                             .GetAwaiter().GetResult();
        EnsureOk(resp);
        using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult());
        var ids = new List<int>();
        if (doc.RootElement.TryGetProperty("workItems", out var wis))
            foreach (var wi in wis.EnumerateArray())
                if (wi.TryGetProperty("id", out var idEl)) ids.Add(idEl.GetInt32());
        return ids.Take(Top).ToList();
    }

    /// <summary>Fetch the full field set for a batch of ids (the REST API caps a GET at 200 ids).</summary>
    private void FetchWorkItems(HttpClient http, List<int> ids, string label, CancellationToken ct)
    {
        foreach (var chunk in Chunk(ids, 200))
        {
            if (ct.IsCancellationRequested) break;
            var url = $"{OrganizationUrl}/_apis/wit/workitems?ids={string.Join(",", chunk)}"
                      + $"&$expand=Fields&api-version={ApiVersion}";
            using var resp = http.GetAsync(url, ct).GetAwaiter().GetResult();
            EnsureOk(resp);
            using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult());
            if (!doc.RootElement.TryGetProperty("value", out var arr)) continue;
            foreach (var wi in arr.EnumerateArray())
            {
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (wi.TryGetProperty("id", out var idEl)) fields["System.Id"] = idEl.ToString();
                if (wi.TryGetProperty("fields", out var f))
                    foreach (var p in f.EnumerateObject()) fields[p.Name] = FlattenValue(p.Value);
                _results.Add(new AdoWorkItemResult(fields, label));
            }
        }
    }

    /// <summary>Download the work items' file attachments (relations with rel "AttachedFile") to a temp
    /// folder, then parse them through the normal file pipeline.</summary>
    private void LoadAttachments(HttpClient http, List<int> ids, CancellationToken ct)
    {
        var temp = TempStorage.GetNewTempPath("adowi");
        int saved = 0, links = 0;
        foreach (var id in ids)
        {
            if (ct.IsCancellationRequested) break;
            var url = $"{OrganizationUrl}/_apis/wit/workitems/{id}?$expand=relations&api-version={ApiVersion}";
            using var resp = http.GetAsync(url, ct).GetAwaiter().GetResult();
            EnsureOk(resp);
            using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult());
            if (!doc.RootElement.TryGetProperty("relations", out var rels)) continue;
            foreach (var rel in rels.EnumerateArray())
            {
                if (!rel.TryGetProperty("rel", out var relType)
                    || !string.Equals(relType.GetString(), "AttachedFile", StringComparison.OrdinalIgnoreCase))
                    continue;
                links++;
                var attUrl = rel.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (string.IsNullOrEmpty(attUrl)) continue;
                var name = rel.TryGetProperty("attributes", out var at) && at.TryGetProperty("name", out var nm)
                    ? nm.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) name = $"attachment_{saved}";
                foreach (var c in Path.GetInvalidFileNameChars()) name = name!.Replace(c, '_');
                try
                {
                    using var dl = http.GetAsync(attUrl, ct).GetAwaiter().GetResult();
                    if (!dl.IsSuccessStatusCode) continue;
                    File.WriteAllBytes(Path.Combine(temp, $"wi{id}_{name}"),
                        dl.Content.ReadAsByteArrayAsync(ct).GetAwaiter().GetResult());
                    saved++;
                }
                catch (Exception ex) { Logger.Instance.Log($"ADO attachment download failed ({attUrl}): {ex.Message}"); }
            }
        }
        if (links == 0) _error = "the selected work item(s) have no file attachments to open.";
        else if (saved == 0) _error = $"found {links} attachment(s) but none downloaded.";
        else _results.AddRange(AttachmentFolderProcessor.ProcessFolder(temp, ct,
            friendlySource: $"ADO {ShortOrg()}/{Project}"));
        Logger.Instance.Log($"ADO attachments: downloaded {saved} file(s), parsed {_results.Count} rows");
    }

    /// <summary>ADO field values are strings/numbers/identity-objects; flatten to a display string.</summary>
    private static string FlattenValue(JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.String: return v.GetString() ?? "";
            case JsonValueKind.Number: return v.GetRawText();
            case JsonValueKind.True: return "true";
            case JsonValueKind.False: return "false";
            case JsonValueKind.Object:
                // Identity refs ("System.ChangedBy" etc.) carry a displayName.
                if (v.TryGetProperty("displayName", out var dn)) return dn.GetString() ?? "";
                return v.GetRawText();
            default: return "";
        }
    }

    private static void EnsureOk(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        var body = "";
        try { body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult(); } catch { }
        var hint = (int)resp.StatusCode switch
        {
            401 or 203 => "authentication failed — check your PAT / sign-in and that it has Work Items (Read) scope.",
            404 => "not found — check the organization URL and project name.",
            _ => resp.ReasonPhrase ?? "request failed",
        };
        throw new Exception($"ADO {(int)resp.StatusCode}: {hint}");
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> src, int size)
    {
        for (int i = 0; i < src.Count; i += size) yield return src.GetRange(i, Math.Min(size, src.Count - i));
    }

    // ----- ISearchLocation -----

    public override void LoadInMemory(CancellationToken cancellationToken = default)
    {
        if (_loaded) return;
        _results.Clear();
        _error = null;
        try
        {
            using var http = CreateClient();
            var ids = ResolveIds(http, cancellationToken);
            var label = GetName();
            if (OpenAttachments) LoadAttachments(http, ids, cancellationToken);
            else if (ids.Count > 0) FetchWorkItems(http, ids, label, cancellationToken);
            Logger.Instance.Log($"ADO loaded {_results.Count} {(OpenAttachments ? "attachment rows" : "work items")} from {OrganizationUrl}/{Project}");
        }
        catch (Exception ex)
        {
            _error = ex.Message.Split('\n')[0].Trim();
            Logger.Instance.Log($"ADO load failed for {OrganizationUrl}/{Project}: {ex}");
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
        CancellationToken cancellationToken = default, int batchSize = 1000)
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

    private string ShortOrg()
    {
        try
        {
            var host = new Uri(OrganizationUrl).Host;            // dev.azure.com OR org.visualstudio.com
            if (host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
                return host.Split('.')[0];
            return OrganizationUrl.TrimEnd('/').Split('/').Last(); // dev.azure.com/<org>
        }
        catch { return OrganizationUrl; }
    }

    public override string GetName()
        => OpenAttachments ? $"ADO: {ShortOrg()}/{Project} attachments" : $"ADO: {ShortOrg()}/{Project}";

    public override string GetDescription()
    {
        if (_error != null) return $"ADO {ShortOrg()}/{Project} — ERROR: {_error}";
        var what = !string.IsNullOrWhiteSpace(Ids) ? $"ids {Ids}"
                 : (string.IsNullOrWhiteSpace(Wiql) ? "recently-changed work items" : "WIQL query");
        return OpenAttachments
            ? $"Log attachments from Azure DevOps {OrganizationUrl}, project {Project}, {what}"
            : $"Azure DevOps {OrganizationUrl}, project {Project}, {what}";
    }

    public string? LastError => _error;

    public override void ClearStatistics() { numRecordsInLastResult = 0; }
    public override List<ReportFromComponent> ReportStatistics() => new();

    public override (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(
        CancellationToken cancellationToken = default)
        => (null, _loaded ? _results.Count : (int?)null);
}
