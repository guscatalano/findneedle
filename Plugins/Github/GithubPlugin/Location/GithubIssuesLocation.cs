using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using findneedle.Implementations;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;

namespace GithubPlugin.Location;

/// <summary>
/// A GitHub repository's issues as a search location. Reads issues over the REST API and maps each to
/// a <see cref="GithubIssueResult"/>. Works on any repo; a token is optional (needed for private repos
/// and to lift the low anonymous rate limit). Pull requests are filtered out (the issues API returns
/// them too). Accepts a repo URL (https://github.com/owner/repo[/issues]) or "owner/repo".
/// </summary>
public class GithubIssuesLocation : ISearchLocation
{
    public string Owner { get; }
    public string Repo { get; }
    public string Token { get; }
    /// <summary>open | closed | all</summary>
    public string State { get; }
    public int MaxItems { get; }
    /// <summary>When &gt; 0, open the log attachments of that one issue instead of listing issues.</summary>
    public int IssueNumber { get; }

    private readonly List<ISearchResult> _results = new();
    private bool _loaded;
    private string? _error;

    public GithubIssuesLocation(string repoOrUrl, string token = "", string state = "all",
                                int maxItems = 500, int issueNumber = 0)
    {
        (Owner, Repo) = ParseRepo(repoOrUrl);
        Token = token ?? "";
        State = string.IsNullOrWhiteSpace(state) ? "all" : state.Trim().ToLowerInvariant();
        MaxItems = maxItems <= 0 ? 500 : maxItems;
        IssueNumber = issueNumber > 0 ? issueNumber : IssueFromUrl(repoOrUrl);
    }

    /// <summary>Pull an issue number out of a .../issues/N URL if present.</summary>
    private static int IssueFromUrl(string input)
    {
        var m = Regex.Match(input ?? "", @"/issues/(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) ? n : 0;
    }

    /// <summary>Parse "owner/repo" out of a github URL or a bare "owner/repo" string.</summary>
    public static (string owner, string repo) ParseRepo(string input)
    {
        var s = (input ?? "").Trim();
        try
        {
            if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var segs = new Uri(s).AbsolutePath.Trim('/').Split('/');
                if (segs.Length >= 2) return (segs[0], segs[1]);
            }
        }
        catch { }
        var parts = s.Trim('/').Split('/');
        return parts.Length >= 2 ? (parts[0], parts[1]) : (s, "");
    }

    private HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("FindNeedle", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(Token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return http;
    }

    public override void LoadInMemory(CancellationToken cancellationToken = default)
    {
        if (_loaded) return;
        _results.Clear();
        _error = null;
        if (IssueNumber > 0) { LoadAttachments(cancellationToken); return; }
        try
        {
            using var http = CreateClient();
            var label = GetName();
            int page = 1;
            while (_results.Count < MaxItems && !cancellationToken.IsCancellationRequested)
            {
                var url = $"https://api.github.com/repos/{Owner}/{Repo}/issues"
                          + $"?state={State}&per_page=100&page={page}";
                using var resp = http.GetAsync(url, cancellationToken).GetAwaiter().GetResult();
                EnsureOk(resp);
                using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());
                if (doc.RootElement.ValueKind != JsonValueKind.Array) break;
                int got = 0;
                foreach (var issue in doc.RootElement.EnumerateArray())
                {
                    got++;
                    // The issues endpoint also returns pull requests — skip them.
                    if (issue.TryGetProperty("pull_request", out _)) continue;
                    _results.Add(new GithubIssueResult(Flatten(issue), label));
                    if (_results.Count >= MaxItems) break;
                }
                if (got < 100) break; // last page
                page++;
            }
            Logger.Instance.Log($"GitHub loaded {_results.Count} issues from {Owner}/{Repo}");
        }
        catch (Exception ex)
        {
            _error = ex.Message.Split('\n')[0].Trim();
            Logger.Instance.Log($"GitHub load failed for {Owner}/{Repo}: {ex}");
        }
        _loaded = true;
        numRecordsInLastResult = _results.Count;
        numRecordsInMemory = _results.Count;
    }

    // Matches GitHub file-upload attachment links in issue/comment markdown (new + legacy forms).
    private static readonly Regex AttachmentRx = new(
        @"https://github\.com/(?:user-attachments/files|[^/\s]+/[^/\s]+/files)/\d+/[^\s)\]""'>]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Open one issue's log attachments: gather attachment URLs from the issue body + comments,
    /// download them to a temp folder, and parse them through the normal file pipeline.</summary>
    private void LoadAttachments(CancellationToken ct)
    {
        try
        {
            using var http = CreateClient();
            var bodies = new List<string>();

            using (var r = http.GetAsync($"https://api.github.com/repos/{Owner}/{Repo}/issues/{IssueNumber}", ct)
                              .GetAwaiter().GetResult())
            {
                EnsureOk(r);
                using var d = JsonDocument.Parse(r.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult());
                if (d.RootElement.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String)
                    bodies.Add(b.GetString() ?? "");
            }
            using (var r = http.GetAsync(
                $"https://api.github.com/repos/{Owner}/{Repo}/issues/{IssueNumber}/comments?per_page=100", ct)
                              .GetAwaiter().GetResult())
            {
                EnsureOk(r);
                using var d = JsonDocument.Parse(r.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult());
                if (d.RootElement.ValueKind == JsonValueKind.Array)
                    foreach (var c in d.RootElement.EnumerateArray())
                        if (c.TryGetProperty("body", out var cb) && cb.ValueKind == JsonValueKind.String)
                            bodies.Add(cb.GetString() ?? "");
            }

            var urls = bodies.SelectMany(b => AttachmentRx.Matches(b).Select(m => m.Value))
                             .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (urls.Count == 0)
            {
                _error = $"issue #{IssueNumber} has no file attachments to open.";
            }
            else
            {
                var temp = TempStorage.GetNewTempPath("ghissue");
                int saved = 0;
                foreach (var url in urls)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var name = SafeFileName(url, saved);
                        using var resp = http.GetAsync(url, ct).GetAwaiter().GetResult();
                        if (!resp.IsSuccessStatusCode) continue;
                        File.WriteAllBytes(Path.Combine(temp, name),
                            resp.Content.ReadAsByteArrayAsync(ct).GetAwaiter().GetResult());
                        saved++;
                    }
                    catch (Exception ex) { Logger.Instance.Log($"GitHub attachment download failed ({url}): {ex.Message}"); }
                }
                if (saved == 0) _error = $"issue #{IssueNumber}: found {urls.Count} attachment link(s) but none downloaded.";
                else _results.AddRange(AttachmentFolderProcessor.ProcessFolder(temp, ct,
                    friendlySource: $"GitHub {Owner}/{Repo}#{IssueNumber}"));
                Logger.Instance.Log($"GitHub issue #{IssueNumber}: downloaded {saved} attachment(s), parsed {_results.Count} rows");
            }
        }
        catch (Exception ex)
        {
            _error = ex.Message.Split('\n')[0].Trim();
            Logger.Instance.Log($"GitHub attachment load failed for {Owner}/{Repo}#{IssueNumber}: {ex}");
        }
        _loaded = true;
        numRecordsInLastResult = _results.Count;
        numRecordsInMemory = _results.Count;
    }

    private static string SafeFileName(string url, int index)
    {
        string name;
        try { name = Path.GetFileName(new Uri(url).AbsolutePath); name = Uri.UnescapeDataString(name); }
        catch { name = ""; }
        if (string.IsNullOrWhiteSpace(name)) name = $"attachment_{index}";
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private static Dictionary<string, string> Flatten(JsonElement issue)
    {
        var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Str(string n) => issue.TryGetProperty(n, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
        f["number"] = issue.TryGetProperty("number", out var num) ? num.ToString() : "";
        f["title"] = Str("title");
        f["state"] = Str("state");
        f["created_at"] = Str("created_at");
        f["updated_at"] = Str("updated_at");
        f["closed_at"] = Str("closed_at");
        f["url"] = Str("html_url");
        if (issue.TryGetProperty("user", out var u) && u.TryGetProperty("login", out var login))
            f["user"] = login.GetString() ?? "";
        if (issue.TryGetProperty("assignee", out var a) && a.ValueKind == JsonValueKind.Object
            && a.TryGetProperty("login", out var al)) f["assignee"] = al.GetString() ?? "";
        if (issue.TryGetProperty("comments", out var c)) f["comments"] = c.ToString();
        if (issue.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
            f["labels"] = string.Join(", ", labels.EnumerateArray()
                .Select(l => l.TryGetProperty("name", out var n) ? n.GetString() : null)
                .Where(n => !string.IsNullOrEmpty(n)));
        var body = Str("body");
        if (!string.IsNullOrEmpty(body)) f["body"] = body.Length > 2000 ? body.Substring(0, 2000) + "…" : body;
        return f;
    }

    private static void EnsureOk(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        var hint = (int)resp.StatusCode switch
        {
            401 => "authentication failed — check your token.",
            403 => "forbidden / rate-limited — add a token to raise the anonymous rate limit.",
            404 => "not found — check the owner/repo (and that a token is set for a private repo).",
            _ => resp.ReasonPhrase ?? "request failed",
        };
        throw new Exception($"GitHub {(int)resp.StatusCode}: {hint}");
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

    public override string GetName()
        => IssueNumber > 0 ? $"GitHub: {Owner}/{Repo}#{IssueNumber} attachments" : $"GitHub: {Owner}/{Repo}";

    public override string GetDescription()
    {
        if (_error != null) return $"GitHub {Owner}/{Repo} — ERROR: {_error}";
        return IssueNumber > 0
            ? $"Log attachments from {Owner}/{Repo} issue #{IssueNumber}"
            : $"GitHub issues for {Owner}/{Repo} (state: {State})";
    }

    public string? LastError => _error;

    public override void ClearStatistics() { numRecordsInLastResult = 0; }
    public override List<ReportFromComponent> ReportStatistics() => new();

    public override (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(
        CancellationToken cancellationToken = default)
        => (null, _loaded ? _results.Count : (int?)null);
}
