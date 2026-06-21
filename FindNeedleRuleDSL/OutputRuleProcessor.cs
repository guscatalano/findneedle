using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FindNeedlePluginLib;
using FindNeedleUmlDsl;
using FindNeedleUmlDsl.PlantUML;
using FindNeedleUmlDsl.MermaidUML;

namespace FindNeedleRuleDSL;

/// <summary>
/// Processes output rules to export search results in various formats (CSV, JSON, XML, TXT).
/// </summary>
public class OutputRuleProcessor
{
    private static readonly string[] DefaultFields = new[]
    {
        "timestamp", "level", "source", "message"
    };

    /// <summary>Absolute paths of every file written during the last <see cref="ProcessOutputRules"/>
    /// call (UML diagrams, rendered images, CSV/JSON/XML/TXT exports). The UI reads this to surface
    /// rule outputs that are otherwise written straight to the output folder with no other handle.</summary>
    public List<string> GeneratedFiles { get; } = new();

    /// <summary>Per-diagram rule-usage info from UML output rules in the last run, keyed by output path.
    /// Lets the UI show which UML rules actually contributed to each generated diagram.</summary>
    public List<UmlDiagramUsage> GeneratedDiagrams { get; } = new();

    /// <summary>Source rows that fed a UML diagram in the last run (row id → matching rule name).
    /// The results viewer uses this to tag the rows that were actually used by the diagram.</summary>
    public List<UmlRowTag> UmlMatchedRows { get; } = new();

    private void RecordGeneratedFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!GeneratedFiles.Any(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)))
            GeneratedFiles.Add(path);
    }

    /// <summary>
    /// Process output rules and write results to files.
    /// </summary>
    public void ProcessOutputRules(
        List<ISearchResult> results,
        IEnumerable<object> outputSections,
        object? ruleSet = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        foreach (var section in outputSections)
        {
            if (cancellationToken.IsCancellationRequested) break;
            try
            {
                ProcessSection(results, section, cancellationToken);
            }
            catch (Exception ex)
            {
                // Ensure full exception is written to the main application log for debugging
                FindNeedlePluginLib.Logger.Instance.Log($"Error processing output section: {ex}");
            }
        }
    }

    private void ProcessSection(List<ISearchResult> results, object section, System.Threading.CancellationToken cancellationToken = default)
    {
        try
        {
            FindNeedlePluginLib.Logger.Instance.Log($"Processing output section of type: {section?.GetType().FullName}");
            if (section is IDictionary<string, object?> d)
            {
                FindNeedlePluginLib.Logger.Instance.Log($"Section keys: {string.Join(",", d.Keys)}");
            }
        }
        catch { }
        // Obtain rules robustly from different section representations (dynamic, dictionary, JsonElement)
        var rules = GetRulesFromSection(section);

        foreach (var ruleObj in rules)
        {
            try
            {
                if (!IsRuleEnabled(ruleObj))
                    continue;

                var action = GetProp(ruleObj, "action") ?? GetProp(ruleObj, "Action");
                var actionType = GetString(action, "type") ?? GetString(action, "Type");
                if (string.IsNullOrEmpty(actionType))
                    continue;
                // allow both standard output actions and the special 'uml' action
                var actionTypeLower = actionType.Trim().ToLowerInvariant();
                if (actionTypeLower != "output" && actionTypeLower != "uml")
                    continue;

                

                // Support UML generation via direct reference to FindNeedleUmlDsl
                if (actionTypeLower == "uml")
                {
                    try
                    {
                        var syntax = GetString(action, "syntax") ?? GetString(action, "Syntax") ?? "mermaid";
                        var umlRulesFile = GetString(action, "rulesFile") ?? GetString(action, "rulesfile");
                        var outPath = ExpandPath(GetString(action, "path") ?? GetString(action, "Path") ?? GetDefaultPath("mmd"));

                        // Create translator instance
                        IUmlSyntaxTranslator translator = syntax.ToLowerInvariant() switch
                        {
                            var s when s == "plantuml" || s == "plant" => new FindNeedleUmlDsl.PlantUML.PlantUmlSyntaxTranslator(),
                            _ => new FindNeedleUmlDsl.MermaidUML.MermaidSyntaxTranslator()
                        };

                        // Create UmlRuleProcessor and invoke
                        var proc = new UmlRuleProcessor(translator);

                        // Load rules file if provided. Try multiple resolution strategies so callers can pass
                        // repository-relative paths (e.g. "FindNeedleUmlDsl/Examples/...") or simple filenames.
                        string? resolvedRulesFile = null;
                        var triedCandidates = new List<string>();
                        if (!string.IsNullOrEmpty(umlRulesFile))
                        {
                            // Normalize separators and trim
                            var umlRulesNorm = umlRulesFile.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();

                            // Try as-is
                            triedCandidates.Add(umlRulesNorm);
                            if (File.Exists(umlRulesNorm)) resolvedRulesFile = umlRulesNorm;

                            // Try relative to application base
                            if (resolvedRulesFile == null)
                            {
                                try
                                {
                                    var candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, umlRulesNorm);
                                    triedCandidates.Add(candidate);
                                    if (File.Exists(candidate)) resolvedRulesFile = candidate;
                                }
                                catch { }
                            }

                            // Try relative to current working directory
                            if (resolvedRulesFile == null)
                            {
                                try
                                {
                                    var candidate = Path.Combine(Environment.CurrentDirectory, umlRulesNorm);
                                    triedCandidates.Add(candidate);
                                    if (File.Exists(candidate)) resolvedRulesFile = candidate;
                                }
                                catch { }
                            }

                            // Try searching for filename under app base (development layout)
                            if (resolvedRulesFile == null)
                            {
                                try
                                {
                                    var fileName = Path.GetFileName(umlRulesNorm);
                                    if (!string.IsNullOrEmpty(fileName))
                                    {
                                        var candidate = Directory.EnumerateFiles(AppDomain.CurrentDomain.BaseDirectory, fileName, SearchOption.AllDirectories).FirstOrDefault();
                                        if (!string.IsNullOrEmpty(candidate))
                                        {
                                            triedCandidates.Add(candidate);
                                            if (File.Exists(candidate)) resolvedRulesFile = candidate;
                                        }
                                    }
                                }
                                catch { }
                            }

                            // Try walking upward from current working directory and resolving the provided path relative to each parent.
                            if (resolvedRulesFile == null)
                            {
                                try
                                {
                                    var parent = new DirectoryInfo(Environment.CurrentDirectory);
                                    while (parent != null)
                                    {
                                        try
                                        {
                                            var candidate = Path.Combine(parent.FullName, umlRulesNorm);
                                            triedCandidates.Add(candidate);
                                            if (File.Exists(candidate))
                                            {
                                                resolvedRulesFile = candidate;
                                                break;
                                            }
                                        }
                                        catch { }
                                        parent = parent.Parent;
                                    }

                                    if (resolvedRulesFile == null)
                                    {
                                        // As last resort try a recursive search under the current directory for the filename only
                                        try
                                        {
                                            var fileNameOnly = Path.GetFileName(umlRulesNorm);
                                            if (!string.IsNullOrEmpty(fileNameOnly))
                                            {
                                                var found = Directory.EnumerateFiles(Environment.CurrentDirectory, fileNameOnly, SearchOption.AllDirectories).FirstOrDefault();
                                                if (!string.IsNullOrEmpty(found))
                                                {
                                                    triedCandidates.Add(found);
                                                    if (File.Exists(found)) resolvedRulesFile = found;
                                                }
                                            }
                                        }
                                        catch { }
                                    }
                                }
                                catch { }
                            }

                            // Try locate repository root (look for .git or a .sln) and resolve relative to it
                            if (resolvedRulesFile == null)
                            {
                                try
                                {
                                    var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                                    DirectoryInfo? repoRoot = null;
                                    while (dir != null)
                                    {
                                        try
                                        {
                                            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || Directory.EnumerateFiles(dir.FullName, "*.sln", SearchOption.TopDirectoryOnly).Any())
                                            {
                                                repoRoot = dir;
                                                break;
                                            }
                                        }
                                        catch { }
                                        dir = dir.Parent;
                                    }
                                    if (repoRoot != null)
                                    {
                                        var candidate = Path.Combine(repoRoot.FullName, umlRulesNorm);
                                        triedCandidates.Add(candidate);
                                        if (File.Exists(candidate)) resolvedRulesFile = candidate;

                                        // If still not found, try searching for directory matching first path segment under repo root
                                        if (resolvedRulesFile == null)
                                        {
                                            var parts = umlRulesNorm.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                                            if (parts.Length > 1)
                                            {
                                                try
                                                {
                                                    var dirName = parts[0];
                                                    var foundDir = Directory.EnumerateDirectories(repoRoot.FullName, dirName, SearchOption.AllDirectories).FirstOrDefault();
                                                    if (!string.IsNullOrEmpty(foundDir))
                                                    {
                                                        var remainder = Path.Combine(parts.Skip(1).ToArray());
                                                        var candidate2 = Path.Combine(foundDir, remainder);
                                                        triedCandidates.Add(candidate2);
                                                        if (File.Exists(candidate2)) resolvedRulesFile = candidate2;
                                                    }
                                                }
                                                catch { }
                                            }

                                            if (resolvedRulesFile == null)
                                            {
                                                // search for filename under repo root
                                                var fileNameOnly = Path.GetFileName(umlRulesNorm);
                                                if (!string.IsNullOrEmpty(fileNameOnly))
                                                {
                                                    var found = Directory.EnumerateFiles(repoRoot.FullName, fileNameOnly, SearchOption.AllDirectories).FirstOrDefault();
                                                    if (!string.IsNullOrEmpty(found))
                                                    {
                                                        triedCandidates.Add(found);
                                                        if (File.Exists(found)) resolvedRulesFile = found;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                catch { }
                            }
                        }

                        // Fallback to default bundled rules file
                        if (resolvedRulesFile == null)
                        {
                            var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rules", "crash-detection-uml.rules.json");
                            if (File.Exists(defaultPath)) resolvedRulesFile = defaultPath;
                        }

                        if (!string.IsNullOrEmpty(resolvedRulesFile))
                        {
                            try
                            {
                                FindNeedlePluginLib.Logger.Instance.Log($"Loading UML rules from: {resolvedRulesFile}");
                                proc.LoadRulesFromFile(resolvedRulesFile);
                            }
                            catch (Exception ex)
                            {
                                FindNeedlePluginLib.Logger.Instance.Log($"Failed to load UML rules from {resolvedRulesFile}: {ex.Message}");
                            }
                        }
                        else
                        {
                            // Log the attempted candidates if available to aid debugging
                            try
                            {
                                if (triedCandidates != null && triedCandidates.Count > 0)
                                {
                                    var joined = string.Join("; ", triedCandidates.Distinct());
                                    FindNeedlePluginLib.Logger.Instance.Log($"No UML rules file found; tried candidates: {joined}");
                                }
                                else
                                {
                                    FindNeedlePluginLib.Logger.Instance.Log("No UML rules file found; proceeding with empty definition (sequence header only)");
                                }
                            }
                            catch
                            {
                                FindNeedlePluginLib.Logger.Instance.Log("No UML rules file found; proceeding with empty definition (sequence header only)");
                            }
                        }

                        // Build list of LogMessage instances. Carry each row's stable id so matches can
                        // be mapped back to the results viewer. Mirror LogLine's id rule: the real row
                        // id when the result has one (>=0), else the load-order index.
                        var msgList = new List<LogMessage>();
                        var idx = 0;
                        foreach (var r in results)
                        {
                            try
                            {
                                long rid;
                                try { rid = r.GetRowId(); } catch { rid = -1; }
                                if (rid < 0) rid = idx;
                                var msg = new LogMessage
                                {
                                    Content = r.GetSearchableData() ?? r.GetMessage() ?? string.Empty,
                                    Source = r.GetResultSource() ?? r.GetSource() ?? string.Empty,
                                    Timestamp = r.GetLogTime(),
                                    RowId = rid,
                                };
                                msgList.Add(msg);
                            }
                            catch { }
                            idx++;
                        }

                        // Call ProcessMessages (timed — surfaced as a generation stat)
                        var genStopwatch = System.Diagnostics.Stopwatch.StartNew();
                        var umlText = proc.ProcessMessages(msgList);
                        genStopwatch.Stop();

                        // Record which source rows fed the diagram (keep the first rule that matched
                        // each row, so a row gets one clear tag).
                        try
                        {
                            foreach (var m in proc.LastMatchedRows ?? new List<UmlRowMatch>())
                            {
                                if (UmlMatchedRows.Any(x => x.RowId == m.RowId)) continue;
                                UmlMatchedRows.Add(new UmlRowTag { RowId = m.RowId, RuleName = m.RuleName });
                            }
                        }
                        catch { }

                        try
                        {
                            // Log diagnostics about generated UML text and rule definition to help debugging empty outputs
                            try
                            {
                                var pCount = proc.Definition.Participants?.Count ?? 0;
                                var rCount = proc.Definition.Rules?.Count ?? 0;
                                FindNeedlePluginLib.Logger.Instance.Log($"UML rule definition: participants={pCount}, rules={rCount}");
                            }
                            catch (Exception ex)
                            {
                                FindNeedlePluginLib.Logger.Instance.Log($"Failed to inspect UmlRuleProcessor definition: {ex.Message}");
                            }

                            if (string.IsNullOrEmpty(umlText))
                            {
                                FindNeedlePluginLib.Logger.Instance.Log("UML generation produced empty text");
                            }
                            else
                            {
                                FindNeedlePluginLib.Logger.Instance.Log($"UML text length: {umlText.Length}");
                                var snippet = umlText.Length > 400 ? umlText.Substring(0, 400) : umlText;
                                FindNeedlePluginLib.Logger.Instance.Log($"UML snippet:\n{snippet}");
                                if (umlText.Trim().Equals("sequenceDiagram", StringComparison.OrdinalIgnoreCase) || umlText.Trim().StartsWith("sequenceDiagram\n", StringComparison.OrdinalIgnoreCase) && umlText.Trim().Split('\n').Length <= 2)
                                {
                                    FindNeedlePluginLib.Logger.Instance.Log("UML output appears minimal (only header). Check rule matching and participants.");
                                }
                            }

                            try
                            {
                                File.WriteAllText(outPath, umlText ?? string.Empty);
                                RecordGeneratedFile(outPath);
                                try
                                {
                                    var lastUsage = proc.LastUsage ?? new List<UmlRuleUsage>();
                                    GeneratedDiagrams.Add(new UmlDiagramUsage
                                    {
                                        FilePath = outPath,
                                        Title = proc.Definition?.Title,
                                        Rules = lastUsage
                                            .Select(u => new UmlRuleHit
                                            {
                                                Name = u.Name,
                                                Match = u.Match,
                                                Count = u.Count,
                                                Lines = (u.Lines ?? new List<UmlMatchedLine>())
                                                    .Select(l => new UmlRuleHitLine { RowId = l.RowId, Content = l.Content })
                                                    .ToList(),
                                            })
                                            .ToList(),
                                        SourceRowCount = msgList.Count,
                                        MatchedRowCount = proc.LastMatchedRows?.Count ?? 0,
                                        ParticipantCount = proc.Definition?.Participants?.Count ?? 0,
                                        InteractionCount = lastUsage.Sum(u => u.Count),
                                        DiagramCharCount = umlText?.Length ?? 0,
                                        DiagramLineCount = string.IsNullOrEmpty(umlText)
                                            ? 0 : umlText.Count(ch => ch == '\n') + 1,
                                        GenerationMs = genStopwatch.ElapsedMilliseconds,
                                    });
                                }
                                catch { }
                                FindNeedlePluginLib.Logger.Instance.Log($"UML markup written: {outPath}");
                            }
                            catch (Exception ex)
                            {
                                FindNeedlePluginLib.Logger.Instance.Log($"Failed to write UML file {outPath}: {ex.Message}");
                            }
                        }
                        catch (Exception ex)
                        {
                            FindNeedlePluginLib.Logger.Instance.Log($"Error processing UML text diagnostics: {ex.Message}");
                        }

                        // Optionally generate image
                        var generateImage = GetBool(action, "generateImage") ?? false;
                        if (generateImage)
                        {
                            try
                            {
                                IUMLGenerator generator = syntax.ToLowerInvariant() switch
                                {
                                    var s when s == "plantuml" || s == "plant" => new FindNeedleUmlDsl.PlantUML.PlantUMLGenerator(),
                                    _ => new FindNeedleUmlDsl.MermaidUML.MermaidUMLGenerator()
                                };

                                var outputGenerated = generator.GenerateUML(outPath);
                                RecordGeneratedFile(outputGenerated);
                                FindNeedlePluginLib.Logger.Instance.Log($"UML generated: {outputGenerated}");
                            }
                            catch (Exception ex)
                            {
                                FindNeedlePluginLib.Logger.Instance.Log($"Error generating UML image: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        FindNeedlePluginLib.Logger.Instance.Log($"Error generating UML: {ex.Message}");
                    }

                    continue; // skip normal output handling for UML action
                }

                var format = GetString(action, "format") ?? GetString(action, "Format") ?? "csv";
                var path = ExpandPath(GetString(action, "path") ?? GetString(action, "Path") ?? GetDefaultPath(format));
                var fields = GetStringList(action, "fields") ?? DefaultFields.ToList();
                var includeHeaders = GetBool(action, "includeHeaders") ?? true;
                var delimiter = GetString(action, "delimiter") ?? ",";
                var pretty = GetBool(action, "pretty") ?? true;

                // Filter results if rule has match pattern. Use regex when provided (supports alternation like "Exception|crash").
                var filteredResults = results;
                var match = GetString(ruleObj, "match") ?? GetString(ruleObj, "Match");
                if (!string.IsNullOrEmpty(match))
                {
                    try
                    {
                        filteredResults = results.Where(r => Regex.IsMatch(r.GetSearchableData() ?? string.Empty, match, RegexOptions.IgnoreCase)).ToList();
                    }
                    catch
                    {
                        // Fallback: treat '|' as separator and check substrings
                        var parts = match.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToArray();
                        filteredResults = results.Where(r => parts.Any(p => (r.GetSearchableData() ?? string.Empty).IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                    }
                }

                // If rule requests tagged output, filter results by applied tags stored in extended properties (if supported)
                var tag = GetString(ruleObj, "tag") ?? GetString(ruleObj, "Tag");
                if (!string.IsNullOrEmpty(tag))
                {
                    // Our ISearchResult doesn't expose tags directly; try to read from result's GetSearchableData / GetMessage or extended dictionary if present
                    filteredResults = filteredResults.Where(r =>
                    {
                        try
                        {
                            // If the result is a dictionary-like object produced by plugins, try reflection
                            var resObj = r as object;
                            var prop = resObj.GetType().GetProperty("Tags");
                            if (prop != null)
                            {
                                var t = prop.GetValue(resObj) as IEnumerable<string>;
                                if (t != null && t.Contains(tag, StringComparer.OrdinalIgnoreCase)) return true;
                            }
                        }
                        catch { }
                        // Fallback: look for tag text in searchable data
                        var sd = r.GetSearchableData() ?? string.Empty;
                        if (sd.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        var msg = r.GetMessage() ?? string.Empty;
                        if (msg.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                        return false;
                    }).ToList();
                }

                WriteOutput(filteredResults, format, path, fields, includeHeaders, delimiter, pretty);
                RecordGeneratedFile(path);
            }
            catch (Exception ex)
            {
                FindNeedlePluginLib.Logger.Instance.Log($"Error processing individual output rule: {ex.Message}");
            }
        }
    }

    private IEnumerable<object> GetRulesFromSection(object section)
    {
        // section may be JsonElement, IDictionary<string,object>, or dynamic object
        if (section == null) return Enumerable.Empty<object>();
        // JsonElement
        if (section is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Object && (je.TryGetProperty("rules", out var r1) || je.TryGetProperty("Rules", out r1)))
            {
                if (r1.ValueKind == JsonValueKind.Array)
                    return r1.EnumerateArray().Select(x => (object)x).ToList();
            }
            return Enumerable.Empty<object>();
        }

        if (section is IDictionary<string, object?> dict)
        {
            if (dict.TryGetValue("rules", out var r) || dict.TryGetValue("Rules", out r))
            {
                if (r is IEnumerable<object> objs) return objs;
                if (r is IEnumerable<dynamic> dyn) return dyn.Cast<object>();
            }
            return Enumerable.Empty<object>();
        }

        // dynamic/POCO
        try
        {
            var prop = section.GetType().GetProperty("Rules") ?? section.GetType().GetProperty("rules");
            if (prop != null)
            {
                var val = prop.GetValue(section);
                if (val is IEnumerable<object> objs) return objs;
                if (val is IEnumerable<dynamic> dyn) return dyn.Cast<object>();
            }
        }
        catch { }

        // Fallback: try serializing the section and parsing JSON to find a "rules" array
        try
        {
            var json = JsonSerializer.Serialize(section);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("rules", out var rulesEl) || doc.RootElement.TryGetProperty("Rules", out rulesEl))
            {
                if (rulesEl.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<object>();
                    foreach (var it in rulesEl.EnumerateArray())
                    {
                        list.Add(it);
                    }
                    return list;
                }
            }
        }
        catch { }

        return Enumerable.Empty<object>();
    }

    private object? GetProp(object? obj, string name)
    {
        if (obj == null) return null;
        if (obj is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Object && je.TryGetProperty(name, out var p)) return p;
            if (je.ValueKind == JsonValueKind.Object && je.TryGetProperty(name.ToLowerInvariant(), out var p2)) return p2;
            return null;
        }
        if (obj is IDictionary<string, object?> dict)
        {
            if (dict.TryGetValue(name, out var v)) return v;
            if (dict.TryGetValue(name.ToLowerInvariant(), out var v2)) return v2;
            return null;
        }
        try
        {
            var pi = obj.GetType().GetProperty(name) ?? obj.GetType().GetProperty(char.ToUpperInvariant(name[0]) + name.Substring(1));
            return pi?.GetValue(obj);
        }
        catch { return null; }
    }

    private string? GetString(object? obj, string name)
    {
        var v = GetProp(obj, name);
        if (v == null) return null;
        if (v is JsonElement ve)
        {
            if (ve.ValueKind == JsonValueKind.String) return ve.GetString();
            return ve.GetRawText();
        }
        return v.ToString();
    }

    private List<string>? GetStringList(object? obj, string name)
    {
        var v = GetProp(obj, name);
        if (v == null) return null;
        if (v is IEnumerable<object> objs) return objs.Select(o => o?.ToString() ?? string.Empty).ToList();
        if (v is JsonElement ve && ve.ValueKind == JsonValueKind.Array) return ve.EnumerateArray().Select(x => x.GetString() ?? x.GetRawText()).ToList();
        return null;
    }

    private bool? GetBool(object? obj, string name)
    {
        var v = GetProp(obj, name);
        if (v == null) return null;
        if (v is bool b) return b;
        if (v is JsonElement ve)
        {
            if (ve.ValueKind == JsonValueKind.True) return true;
            if (ve.ValueKind == JsonValueKind.False) return false;
            if (ve.ValueKind == JsonValueKind.String && bool.TryParse(ve.GetString(), out var pb)) return pb;
            return null;
        }
        if (v is string s && bool.TryParse(s, out var pb2)) return pb2;
        return null;
    }

    private bool IsRuleEnabled(object? rule)
    {
        var en = GetBool(rule, "enabled") ?? GetBool(rule, "Enabled");
        return en ?? true;
    }

    private void WriteOutput(
        List<ISearchResult> results,
        string format,
        string path,
        List<string> fields,
        bool includeHeaders,
        string delimiter,
        bool pretty)
    {
        // Ensure directory exists
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        switch (format.ToLower())
        {
            case "csv":
                WriteCsv(results, path, fields, includeHeaders, delimiter);
                break;
            case "json":
                WriteJson(results, path, fields, pretty);
                break;
            case "xml":
                WriteXml(results, path, fields, pretty);
                break;
            case "txt":
            case "text":
                WriteTxt(results, path, fields);
                break;
            default:
                System.Diagnostics.Debug.WriteLine($"Unknown output format: {format}");
                break;
        }

        // Write to main application log so it's included in the detailed log and optionally echoed to console
        FindNeedlePluginLib.Logger.Instance.Log($"Output written: {path} ({results.Count} results)");
    }

    private void WriteCsv(List<ISearchResult> results, string path, List<string> fields, bool includeHeaders, string delimiter)
    {
        var sb = new StringBuilder();

        if (includeHeaders)
        {
            sb.AppendLine(string.Join(delimiter, fields.Select(EscapeCsvField)));
        }

        foreach (var result in results)
        {
            var values = fields.Select(f => EscapeCsvField(GetFieldValue(result, f)));
            sb.AppendLine(string.Join(delimiter, values));
        }

        File.WriteAllText(path, sb.ToString());
    }

    private void WriteJson(List<ISearchResult> results, string path, List<string> fields, bool pretty)
    {
        var items = results.Select(result =>
        {
            var dict = new Dictionary<string, object>();
            foreach (var field in fields)
            {
                dict[field] = GetFieldValue(result, field);
            }
            return dict;
        }).ToList();

        var options = new JsonSerializerOptions
        {
            WriteIndented = pretty
        };

        var json = JsonSerializer.Serialize(items, options);
        File.WriteAllText(path, json);
    }

    private void WriteXml(List<ISearchResult> results, string path, List<string> fields, bool pretty)
    {
        var root = new XElement("Results");

        foreach (var result in results)
        {
            var item = new XElement("Result");
            foreach (var field in fields)
            {
                item.Add(new XElement(SanitizeXmlName(field), GetFieldValue(result, field)));
            }
            root.Add(item);
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        
        if (pretty)
        {
            doc.Save(path);
        }
        else
        {
            File.WriteAllText(path, doc.ToString(SaveOptions.DisableFormatting));
        }
    }

    private void WriteTxt(List<ISearchResult> results, string path, List<string> fields)
    {
        var sb = new StringBuilder();

        foreach (var result in results)
        {
            var parts = fields.Select(f => $"{f}={GetFieldValue(result, f)}");
            sb.AppendLine(string.Join(" | ", parts));
        }

        File.WriteAllText(path, sb.ToString());
    }

    private string GetFieldValue(ISearchResult result, string field)
    {
        return field.ToLower() switch
        {
            "timestamp" or "time" or "logtime" => result.GetLogTime().ToString("yyyy-MM-dd HH:mm:ss"),
            "level" => result.GetLevel().ToString(),
            "source" => result.GetSource(),
            "message" or "msg" => result.GetMessage(),
            "searchable" or "data" => result.GetSearchableData(),
            "machine" or "machinename" => result.GetMachineName(),
            "username" or "user" => result.GetUsername(),
            "taskname" or "task" => result.GetTaskName(),
            "opcode" => result.GetOpCode(),
            "resultsource" or "file" => result.GetResultSource(),
            _ => result.GetSearchableData() // Default to searchable data
        };
    }

    private string ExpandPath(string path)
    {
        var now = DateTime.Now;
        var outputBase = AppDomain.CurrentDomain.BaseDirectory;
        var outputFolder = Path.Combine(outputBase, "output");
        if (!Directory.Exists(outputFolder))
        {
            try { Directory.CreateDirectory(outputFolder); } catch { }
        }

        return path
            .Replace("{date}", now.ToString("yyyy-MM-dd"))
            .Replace("{time}", now.ToString("HHmmss"))
            .Replace("{datetime}", now.ToString("yyyy-MM-dd_HHmmss"))
            // {output} maps to application "output" folder
            .Replace("{output}", outputFolder.TrimEnd(Path.DirectorySeparatorChar))
            // keep {temp} for backward compatibility but map it to output folder as requested
            .Replace("{temp}", outputFolder.TrimEnd(Path.DirectorySeparatorChar));
    }

    private string GetDefaultPath(string format)
    {
        var ext = format.ToLower() switch
        {
            "json" => ".json",
            "xml" => ".xml",
            "txt" or "text" => ".txt",
            _ => ".csv"
        };
        return Path.Combine(Path.GetTempPath(), $"findneedle_output_{DateTime.Now:yyyyMMdd_HHmmss}{ext}");
    }

    private string EscapeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private string SanitizeXmlName(string name)
    {
        // Replace invalid XML element name characters
        var sanitized = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sanitized.Append(c);
            else
                sanitized.Append('_');
        }
        
        // Ensure it starts with a letter or underscore
        if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
            sanitized.Insert(0, '_');
            
        return sanitized.Length > 0 ? sanitized.ToString() : "field";
    }
}

/// <summary>Rule-usage info for one generated UML diagram (which rules fired and how often),
/// plus generation stats (inputs, output size, timing) the UI surfaces after a Generate.</summary>
public sealed class UmlDiagramUsage
{
    public string FilePath { get; set; } = string.Empty;
    public string? Title { get; set; }
    public List<UmlRuleHit> Rules { get; set; } = new();

    // ----- Generation stats -----
    /// <summary>Rows fed to the generator (the search's result set at generation time).</summary>
    public int SourceRowCount { get; set; }
    /// <summary>Rows that matched a UML rule and so contributed to the diagram.</summary>
    public int MatchedRowCount { get; set; }
    /// <summary>Participants (lifelines / actors) in the generated diagram.</summary>
    public int ParticipantCount { get; set; }
    /// <summary>Total rule hits = interactions drawn (sum of per-rule counts).</summary>
    public int InteractionCount { get; set; }
    /// <summary>Lines in the generated markup.</summary>
    public int DiagramLineCount { get; set; }
    /// <summary>Characters in the generated markup.</summary>
    public int DiagramCharCount { get; set; }
    /// <summary>Wall-clock time spent generating the diagram markup, in milliseconds.</summary>
    public long GenerationMs { get; set; }
}

public sealed class UmlRuleHit
{
    public string Name { get; set; } = string.Empty;
    public string Match { get; set; } = string.Empty;
    public int Count { get; set; }
    public List<UmlRuleHitLine> Lines { get; set; } = new();
}

public sealed class UmlRuleHitLine
{
    public long RowId { get; set; } = -1;
    public string Content { get; set; } = string.Empty;
}

/// <summary>A results-viewer row that fed a UML diagram (stable row id + the rule that matched it).</summary>
public sealed class UmlRowTag
{
    public long RowId { get; set; }
    public string RuleName { get; set; } = string.Empty;
}
