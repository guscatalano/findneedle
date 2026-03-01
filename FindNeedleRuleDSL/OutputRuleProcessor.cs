using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FindNeedlePluginLib;
using System.Reflection;

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

    /// <summary>
    /// Process output rules and write results to files.
    /// </summary>
    public void ProcessOutputRules(
        List<ISearchResult> results,
        IEnumerable<object> outputSections,
        object? ruleSet = null)
    {
        foreach (var section in outputSections)
        {
            try
            {
                ProcessSection(results, section);
            }
            catch (Exception ex)
            {
                // Ensure full exception is written to the main application log for debugging
                FindNeedlePluginLib.Logger.Instance.Log($"Error processing output section: {ex}");
            }
        }
    }

    private void ProcessSection(List<ISearchResult> results, object section)
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

                

                // Support UML generation via late-binding to FindNeedleUmlDsl assembly (avoids compile-time project refs)
                if (actionTypeLower == "uml")
                {
                    try
                    {
                        var syntax = GetString(action, "syntax") ?? GetString(action, "Syntax") ?? "mermaid";
                        var umlRulesFile = GetString(action, "rulesFile") ?? GetString(action, "rulesfile");
                        var outPath = ExpandPath(GetString(action, "path") ?? GetString(action, "Path") ?? GetDefaultPath("mmd"));

                        // Load FindNeedleUmlDsl assembly if present alongside the app or elsewhere in the repo
                        Assembly? umlAsm = null;
                        try
                        {
                            var asmPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FindNeedleUmlDsl.dll");
                            if (File.Exists(asmPath))
                            {
                                umlAsm = Assembly.LoadFrom(asmPath);
                            }
                            else
                            {
                                // Attempt to locate the DLL anywhere under the app base directory (development scenario)
                                try
                                {
                                    var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                                    var candidate = Directory.EnumerateFiles(baseDir, "FindNeedleUmlDsl.dll", SearchOption.AllDirectories).FirstOrDefault();
                                    if (!string.IsNullOrEmpty(candidate))
                                    {
                                        umlAsm = Assembly.LoadFrom(candidate);
                                        FindNeedlePluginLib.Logger.Instance.Log($"Resolved FindNeedleUmlDsl to: {candidate}");
                                    }
                                }
                                catch { }
                            }
                        }
                        catch (Exception ex)
                        {
                            FindNeedlePluginLib.Logger.Instance.Log($"Error loading FindNeedleUmlDsl assembly: {ex.Message}");
                        }

                        if (umlAsm == null)
                        {
                            FindNeedlePluginLib.Logger.Instance.Log("UML generation skipped: FindNeedleUmlDsl assembly not found. Build the FindNeedleUmlDsl project or copy FindNeedleUmlDsl.dll into the application output folder.");
                            continue;
                        }

                        // Build list of LogMessage instances
                        var logMsgType = umlAsm.GetType("FindNeedleUmlDsl.LogMessage");
                        if (logMsgType == null)
                        {
                            FindNeedlePluginLib.Logger.Instance.Log("UML generation skipped: LogMessage type not found in FindNeedleUmlDsl");
                            continue;
                        }

                        var listType = typeof(List<>).MakeGenericType(logMsgType);
                        var msgList = Activator.CreateInstance(listType)!; // keep as object to avoid invalid cast
                        var addMethod = listType.GetMethod("Add");

                        foreach (var r in results)
                        {
                            try
                            {
                                var msg = Activator.CreateInstance(logMsgType)!;
                                var propContent = logMsgType.GetProperty("Content");
                                var propSource = logMsgType.GetProperty("Source");
                                var propTs = logMsgType.GetProperty("Timestamp");
                                propContent?.SetValue(msg, r.GetSearchableData() ?? r.GetMessage() ?? string.Empty);
                                propSource?.SetValue(msg, r.GetResultSource() ?? r.GetSource() ?? string.Empty);
                                propTs?.SetValue(msg, r.GetLogTime());
                                addMethod?.Invoke(msgList, new object[] { msg });
                            }
                            catch { }
                        }

                        // Create translator instance
                        Type? translatorType = syntax.ToLowerInvariant() switch
                        {
                            var s when s == "plantuml" || s == "plant" => umlAsm.GetType("FindNeedleUmlDsl.PlantUML.PlantUmlSyntaxTranslator"),
                            _ => umlAsm.GetType("FindNeedleUmlDsl.MermaidUML.MermaidSyntaxTranslator")
                        };

                        if (translatorType == null)
                        {
                            FindNeedlePluginLib.Logger.Instance.Log("UML generation skipped: translator type not found");
                            continue;
                        }

                        var translator = Activator.CreateInstance(translatorType)!;

                        // Create UmlRuleProcessor and invoke
                        var procType = umlAsm.GetType("FindNeedleUmlDsl.UmlRuleProcessor");
                        if (procType == null)
                        {
                            FindNeedlePluginLib.Logger.Instance.Log("UML generation skipped: UmlRuleProcessor type not found");
                            continue;
                        }

                        var proc = Activator.CreateInstance(procType, new object[] { translator })!;

                        // Load rules file if provided. Try multiple resolution strategies so callers can pass
                        // repository-relative paths (e.g. "FindNeedleUmlDsl/Examples/...") or simple filenames.
                        var loadMethod = procType.GetMethod("LoadRulesFromFile");
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

                        if (!string.IsNullOrEmpty(resolvedRulesFile) && loadMethod != null)
                        {
                            try
                            {
                                FindNeedlePluginLib.Logger.Instance.Log($"Loading UML rules from: {resolvedRulesFile}");
                                loadMethod.Invoke(proc, new object[] { resolvedRulesFile });
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

                        // Call ProcessMessages
                        var procMethod = procType.GetMethod("ProcessMessages");
                        if (procMethod == null)
                        {
                            FindNeedlePluginLib.Logger.Instance.Log("UML generation skipped: ProcessMessages method not found");
                            continue;
                        }

                        var umlText = procMethod.Invoke(proc, new object[] { msgList }) as string;
                        try
                        {
                            // Log diagnostics about generated UML text and rule definition to help debugging empty outputs
                            try
                            {
                                var defField = proc.GetType().GetField("_definition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (defField != null)
                                {
                                    var def = defField.GetValue(proc);
                                    if (def != null)
                                    {
                                        var partsProp = def.GetType().GetProperty("Participants");
                                        var rulesProp = def.GetType().GetProperty("Rules");
                                        var pCount = partsProp?.GetValue(def) is System.Collections.ICollection pc ? pc.Count : 0;
                                        var rCount = rulesProp?.GetValue(def) is System.Collections.ICollection rc ? rc.Count : 0;
                                        FindNeedlePluginLib.Logger.Instance.Log($"UML rule definition: participants={pCount}, rules={rCount}");
                                    }
                                }
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

                        // Optionally generate image by creating generator from the same assembly
                        var generateImage = GetBool(action, "generateImage") ?? false;
                        if (generateImage)
                        {
                            try
                            {
                                Type? genType = syntax.ToLowerInvariant() switch
                                {
                                    var s when s == "plantuml" || s == "plant" => umlAsm.GetType("FindNeedleUmlDsl.PlantUML.PlantUMLGenerator"),
                                    _ => umlAsm.GetType("FindNeedleUmlDsl.MermaidUML.MermaidUMLGenerator")
                                };
                                if (genType != null)
                                {
                                    object? gen = null;
                                    // Prefer creating generator with installer injected (so it can detect installed tools)
                                    try
                                    {
                                        object? installerInstance = null;
                                        // Try to load FindNeedleToolInstallers from app output and create installer instance to inject
                                        try
                                        {
                                            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                                            var installerAsmPath = Directory.EnumerateFiles(baseDir, "FindNeedleToolInstallers.dll", SearchOption.AllDirectories).FirstOrDefault();
                                            Assembly? instAsm = null;
                                            if (!string.IsNullOrEmpty(installerAsmPath) && File.Exists(installerAsmPath))
                                            {
                                                try { instAsm = Assembly.LoadFrom(installerAsmPath); } catch { instAsm = null; }
                                            }

                                            Type? installerType = null;
                                            if (instAsm != null)
                                            {
                                                if (genType.FullName != null && genType.FullName.Contains("MermaidUML"))
                                                    installerType = instAsm.GetType("FindNeedleToolInstallers.MermaidInstaller");
                                                else if (genType.FullName != null && genType.FullName.Contains("PlantUML"))
                                                    installerType = instAsm.GetType("FindNeedleToolInstallers.PlantUmlInstaller");
                                            }

                                            // Fallback: search already-loaded assemblies
                                            if (installerType == null)
                                            {
                                                var asmList = AppDomain.CurrentDomain.GetAssemblies();
                                                if (genType.FullName != null && genType.FullName.Contains("MermaidUML"))
                                                {
                                                    installerType = asmList.SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } }).FirstOrDefault(t => t.FullName == "FindNeedleToolInstallers.MermaidInstaller");
                                                }
                                                else if (genType.FullName != null && genType.FullName.Contains("PlantUML"))
                                                {
                                                    installerType = asmList.SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } }).FirstOrDefault(t => t.FullName == "FindNeedleToolInstallers.PlantUmlInstaller");
                                                }
                                            }

                                            if (installerType != null)
                                            {
                                                try
                                                {
                                                    // try parameterless
                                                    installerInstance = Activator.CreateInstance(installerType);
                                                }
                                                catch
                                                {
                                                    try
                                                    {
                                                        // try constructors with null args
                                                        var ctors = installerType.GetConstructors().OrderBy(c => c.GetParameters().Length).ToList();
                                                        foreach (var c in ctors)
                                                        {
                                                            var ps = c.GetParameters();
                                                            var ctorArgs = ps.Select(p => (object?)null).ToArray();
                                                            try { installerInstance = c.Invoke(ctorArgs); if (installerInstance != null) break; } catch { }
                                                        }
                                                    }
                                                    catch { }
                                                }
                                            }

                                            if (installerInstance != null)
                                            {
                                                try { gen = Activator.CreateInstance(genType, new object[] { installerInstance }); }
                                                catch { try { gen = Activator.CreateInstance(genType); } catch { } }
                                            }
                                            else
                                            {
                                                try { gen = Activator.CreateInstance(genType); } catch { }
                                            }
                                        }
                                        catch
                                        {
                                            try { gen = Activator.CreateInstance(genType); } catch { }
                                        }
                                    }
                                    catch
                                    {
                                        // Try to find any constructor and invoke it with nulls for parameters
                                        try
                                        {
                                            var ctors = genType.GetConstructors().OrderBy(c => c.GetParameters().Length).ToList();
                                            foreach (var c in ctors)
                                            {
                                                var ps = c.GetParameters();
                                                var args = ps.Select(p => (object?)null).ToArray();
                                                try
                                                {
                                                    gen = c.Invoke(args);
                                                    if (gen != null) break;
                                                }
                                                catch { }
                                            }
                                        }
                                        catch { }
                                    }

                                    if (gen != null)
                                    {
                                        // Attempt to inject a concrete installer instance into the generator instance
                                        try
                                        {
                                            var baseDir2 = AppDomain.CurrentDomain.BaseDirectory;
                                            var installerAsmPath2 = Directory.EnumerateFiles(baseDir2, "FindNeedleToolInstallers.dll", SearchOption.AllDirectories).FirstOrDefault();
                                            if (!string.IsNullOrEmpty(installerAsmPath2) && File.Exists(installerAsmPath2))
                                            {
                                                try
                                                {
                                                    var instAsm2 = Assembly.LoadFrom(installerAsmPath2);
                                                    Type? installerType2 = null;
                                                    if (genType.FullName != null && genType.FullName.Contains("MermaidUML"))
                                                        installerType2 = instAsm2.GetType("FindNeedleToolInstallers.MermaidInstaller");
                                                    else if (genType.FullName != null && genType.FullName.Contains("PlantUML"))
                                                        installerType2 = instAsm2.GetType("FindNeedleToolInstallers.PlantUmlInstaller");

                                                    if (installerType2 != null)
                                                    {
                                                        object? installerInstance2 = null;
                                                        try { installerInstance2 = Activator.CreateInstance(installerType2); }
                                                        catch
                                                        {
                                                            try
                                                            {
                                                                var ctors2 = installerType2.GetConstructors().OrderBy(c => c.GetParameters().Length).ToList();
                                                                foreach (var c2 in ctors2)
                                                                {
                                                                    var ps2 = c2.GetParameters();
                                                                    var ctorArgs2 = ps2.Select(p => (object?)null).ToArray();
                                                                    try { installerInstance2 = c2.Invoke(ctorArgs2); if (installerInstance2 != null) break; } catch { }
                                                                }
                                                            }
                                                            catch { }
                                                        }

                                                        if (installerInstance2 != null)
                                                        {
                                                            try
                                                            {
                                                                var instField = gen.GetType().GetField("_installer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                                                if (instField != null)
                                                                {
                                                                    instField.SetValue(gen, installerInstance2);
                                                                    FindNeedlePluginLib.Logger.Instance.Log($"Injected installer into generator instance: {installerInstance2.GetType().FullName}");
                                                                }
                                                            }
                                                            catch (Exception ie)
                                                            {
                                                                FindNeedlePluginLib.Logger.Instance.Log($"Failed to set _installer on generator instance: {ie.Message}");
                                                            }
                                                        }
                                                    }
                                                }
                                                catch { }
                                            }
                                        }
                                        catch { }
                                        try
                                        {
                                            var genMethod2 = genType.GetMethod("GenerateUML", new Type[] { typeof(string) });
                                            object? outputGenerated2 = null;
                                            if (genMethod2 != null)
                                            {
                                                outputGenerated2 = genMethod2.Invoke(gen, new object[] { outPath });
                                            }
                                            else
                                            {
                                                // Fallback: try any GenerateUML overload
                                                var methods = genType.GetMethods().Where(m => m.Name == "GenerateUML").ToList();
                                                if (methods.Count > 0)
                                                {
                                                    var m = methods[0];
                                                    var parameters = m.GetParameters();
                                                    var args = new List<object?>();
                                                    foreach (var p in parameters)
                                                    {
                                                        if (p.ParameterType == typeof(string)) args.Add(outPath);
                                                        else args.Add(null);
                                                    }
                                                    outputGenerated2 = m.Invoke(gen, args.ToArray());
                                                }
                                            }

                                            var genNameProp = genType.GetProperty("Name");
                                            var genName = genNameProp?.GetValue(gen)?.ToString() ?? genType.Name;
                                            FindNeedlePluginLib.Logger.Instance.Log($"UML generated by {genName}: {outputGenerated2}");
                                        }
                                        catch (Exception ex)
                                        {
                                            FindNeedlePluginLib.Logger.Instance.Log($"Error invoking UML generator: {ex.Message}");
                                            // If generator failed (likely missing external tool), try to run bundled installers and retry once
                                            try
                                            {
                                                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                                                var installerAsmPath = Directory.EnumerateFiles(baseDir, "FindNeedleToolInstallers.dll", SearchOption.AllDirectories).FirstOrDefault();
                                                if (!string.IsNullOrEmpty(installerAsmPath) && File.Exists(installerAsmPath))
                                                {
                                                    var instAsm = Assembly.LoadFrom(installerAsmPath);
                                                    var managerType = instAsm.GetType("FindNeedleToolInstallers.UmlDependencyManager");
                                                    if (managerType != null)
                                                    {
                                                        object? manager = null;
                                                        try
                                                        {
                                                            manager = Activator.CreateInstance(managerType);
                                                        }
                                                        catch
                                                        {
                                                            try
                                                            {
                                                                var ctors = managerType.GetConstructors().OrderBy(c => c.GetParameters().Length).ToList();
                                                                foreach (var c in ctors)
                                                                {
                                                                    var ps = c.GetParameters();
                                                                    var args = ps.Select(p => (object?)null).ToArray();
                                                                    try
                                                                    {
                                                                        manager = c.Invoke(args);
                                                                        if (manager != null) break;
                                                                    }
                                                                    catch { }
                                                                }
                                                            }
                                                            catch { }
                                                        }
                                                        var installMethod = managerType.GetMethod("InstallAllMissingAsync");
                                                        if (installMethod != null && manager != null)
                                                        {
                                                            FindNeedlePluginLib.Logger.Instance.Log("Attempting to install missing UML dependencies (retry)");
                                                            var task = (System.Threading.Tasks.Task)installMethod.Invoke(manager, new object[] { null, System.Threading.CancellationToken.None })!;
                                                            task.Wait();
                                                            // If installer returns paths, add them to PATH for this process so generators can find installed tools
                                                            try
                                                            {
                                                                var resultProp = task.GetType().GetProperty("Result");
                                                                var resultObj = resultProp?.GetValue(task);
                                                                if (resultObj is System.Collections.IDictionary dict)
                                                                {
                                                                    var pathEntries = new List<string>();
                                                                    foreach (System.Collections.DictionaryEntry de in dict)
                                                                    {
                                                                        try
                                                                        {
                                                                            var installResult = de.Value;
                                                                            if (installResult == null)
                                                                            {
                                                                                FindNeedlePluginLib.Logger.Instance.Log($"Installer produced null result for key: {de.Key}");
                                                                                continue;
                                                                            }
                                                                            var successProp = installResult.GetType().GetProperty("Success");
                                                                            var pathProp = installResult.GetType().GetProperty("Path");
                                                                            var messageProp = installResult.GetType().GetProperty("Message");
                                                                            var successVal = successProp?.GetValue(installResult)?.ToString() ?? "?";
                                                                            var installedPath = pathProp?.GetValue(installResult) as string;
                                                                            var msg = messageProp?.GetValue(installResult) as string;
                                                                            FindNeedlePluginLib.Logger.Instance.Log($"Installer result for {de.Key}: Success={successVal}, Path={installedPath ?? "(null)"}, Message={msg ?? "(none)"}");
                                                                            if (!string.IsNullOrEmpty(installedPath))
                                                                            {
                                                                                var dir = Path.GetDirectoryName(installedPath);
                                                                                if (!string.IsNullOrEmpty(dir)) pathEntries.Add(dir);
                                                                            }
                                                                        }
                                                                        catch { }
                                                                    }

                                                                    // If installer returned explicit tool paths, set generator cached fields so they don't rely on PATH
                                                                    try
                                                                    {
                                                                        foreach (var pe in pathEntries)
                                                                        {
                                                                            try
                                                                            {
                                                                                var lower = pe.ToLowerInvariant();
                                                                                // Mermaid: look for mmdc or mermaid
                                                                                if (genType != null && genType.FullName != null && genType.FullName.Contains("MermaidUML", StringComparison.OrdinalIgnoreCase))
                                                                                {
                                                                                    var mmdcField = genType.GetField("_cachedMermaidCliPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                                                                                    if (mmdcField != null)
                                                                                    {
                                                                                        // find mmdc executable under this directory
                                                                                        var found = Directory.EnumerateFiles(pe, "mmdc*", SearchOption.TopDirectoryOnly).FirstOrDefault();
                                                                                        if (found == null)
                                                                                        {
                                                                                            // try .cmd
                                                                                            found = Directory.EnumerateFiles(pe, "mmdc.cmd", SearchOption.TopDirectoryOnly).FirstOrDefault();
                                                                                        }
                                                                                        if (!string.IsNullOrEmpty(found))
                                                                                        {
                                                                                            mmdcField.SetValue(null, found);
                                                                                            FindNeedlePluginLib.Logger.Instance.Log($"Set Mermaid cached mmdc path: {found}");
                                                                                        }
                                                                                    }

                                                                                    var nodeField = genType.GetField("_cachedNodePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                                                                                    if (nodeField != null)
                                                                                    {
                                                                                        var foundNode = Directory.EnumerateFiles(pe, "node.exe", SearchOption.AllDirectories).FirstOrDefault();
                                                                                        if (!string.IsNullOrEmpty(foundNode))
                                                                                        {
                                                                                            nodeField.SetValue(null, foundNode);
                                                                                            FindNeedlePluginLib.Logger.Instance.Log($"Set Mermaid cached node path: {foundNode}");
                                                                                        }
                                                                                    }
                                                                                }

                                                                                // PlantUML: set jar/java
                                                                                if (genType != null && genType.FullName != null && genType.FullName.Contains("PlantUML", StringComparison.OrdinalIgnoreCase))
                                                                                {
                                                                                    var plantField = genType.GetField("_cachedPlantUMLPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                                                                                    if (plantField != null)
                                                                                    {
                                                                                        var foundJar = Directory.EnumerateFiles(pe, "*.jar", SearchOption.TopDirectoryOnly).FirstOrDefault();
                                                                                        if (!string.IsNullOrEmpty(foundJar))
                                                                                        {
                                                                                            plantField.SetValue(null, foundJar);
                                                                                            FindNeedlePluginLib.Logger.Instance.Log($"Set PlantUML cached jar path: {foundJar}");
                                                                                        }
                                                                                    }

                                                                                    var javaField = genType.GetField("_cachedJavaPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                                                                                    if (javaField != null)
                                                                                    {
                                                                                        var foundJava = Directory.EnumerateFiles(pe, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
                                                                                        if (!string.IsNullOrEmpty(foundJava))
                                                                                        {
                                                                                            javaField.SetValue(null, foundJava);
                                                                                            FindNeedlePluginLib.Logger.Instance.Log($"Set PlantUML cached java path: {foundJava}");
                                                                                        }
                                                                                    }
                                                                                }
                                                                            }
                                                                            catch { }
                                                                        }
                                                                    }
                                                                    catch { }

                                                                    if (pathEntries.Count > 0)
                                                                    {
                                                                        try
                                                                        {
                                                                            var cur = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                                                                            var parts = cur.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
                                                                            var toAdd = pathEntries.Where(p => !parts.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
                                                                            if (toAdd.Count > 0)
                                                                            {
                                                                                var newPath = string.Join(Path.PathSeparator, toAdd) + Path.PathSeparator + cur;
                                                                                Environment.SetEnvironmentVariable("PATH", newPath);
                                                                                FindNeedlePluginLib.Logger.Instance.Log($"Prepended installer paths to PATH: {string.Join(";", toAdd)}");
                                                                            }
                                                                        }
                                                                        catch (Exception pathEx)
                                                                        {
                                                                            FindNeedlePluginLib.Logger.Instance.Log($"Failed to update PATH with installer results: {pathEx.Message}");
                                                                        }
                                                                    }
                                                                }
                                                            }
                                                            catch { }

                                                            FindNeedlePluginLib.Logger.Instance.Log("Installer finished, retrying UML generation");

                                                            // Try to create generator again and invoke
                                                            object? gen2 = null;
                                                            try
                                                            {
                                                                object? installerInstance = null;
                                                                var asmList = AppDomain.CurrentDomain.GetAssemblies();
                                                                if (genType.FullName != null && genType.FullName.Contains("MermaidUML"))
                                                                {
                                                                    var installerType = asmList.SelectMany(a =>
                                                                    {
                                                                        try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
                                                                    }).FirstOrDefault(t => t.FullName == "FindNeedleToolInstallers.MermaidInstaller");
                                                                    if (installerType != null)
                                                                    {
                                                                        try { installerInstance = Activator.CreateInstance(installerType); } catch { }
                                                                    }
                                                                }
                                                                else if (genType.FullName != null && genType.FullName.Contains("PlantUML"))
                                                                {
                                                                    var installerType = asmList.SelectMany(a =>
                                                                    {
                                                                        try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
                                                                    }).FirstOrDefault(t => t.FullName == "FindNeedleToolInstallers.PlantUmlInstaller");
                                                                    if (installerType != null)
                                                                    {
                                                                        try { installerInstance = Activator.CreateInstance(installerType); } catch { }
                                                                    }
                                                                }

                                                                if (installerInstance != null)
                                                                {
                                                                    try { gen2 = Activator.CreateInstance(genType, new object[] { installerInstance }); } catch { }
                                                                }

                                                                if (gen2 == null)
                                                                {
                                                                    gen2 = Activator.CreateInstance(genType);
                                                                }
                                                            }
                                                            catch
                                                            {
                                                                try
                                                                {
                                                                    var ctors = genType.GetConstructors().OrderBy(c => c.GetParameters().Length).ToList();
                                                                    foreach (var c in ctors)
                                                                    {
                                                                        var ps = c.GetParameters();
                                                                        var args = ps.Select(p => (object?)null).ToArray();
                                                                        try { gen2 = c.Invoke(args); if (gen2 != null) break; } catch { }
                                                                    }
                                                                }
                                                                catch { }
                                                            }

                                                            if (gen2 != null)
                                                            {
                                                                // Attempt to inject installer into retry-generated instance as well
                                                                try
                                                                {
                                                                    var baseDir3 = AppDomain.CurrentDomain.BaseDirectory;
                                                                    var installerAsmPath3 = Directory.EnumerateFiles(baseDir3, "FindNeedleToolInstallers.dll", SearchOption.AllDirectories).FirstOrDefault();
                                                                    if (!string.IsNullOrEmpty(installerAsmPath3) && File.Exists(installerAsmPath3))
                                                                    {
                                                                        try
                                                                        {
                                                                            var instAsm3 = Assembly.LoadFrom(installerAsmPath3);
                                                                            Type? installerType3 = null;
                                                                            if (genType.FullName != null && genType.FullName.Contains("MermaidUML"))
                                                                                installerType3 = instAsm3.GetType("FindNeedleToolInstallers.MermaidInstaller");
                                                                            else if (genType.FullName != null && genType.FullName.Contains("PlantUML"))
                                                                                installerType3 = instAsm3.GetType("FindNeedleToolInstallers.PlantUmlInstaller");

                                                                            if (installerType3 != null)
                                                                            {
                                                                                object? installerInstance3 = null;
                                                                                try { installerInstance3 = Activator.CreateInstance(installerType3); }
                                                                                catch
                                                                                {
                                                                                    try
                                                                                    {
                                                                                        var ctorss = installerType3.GetConstructors().OrderBy(c => c.GetParameters().Length).ToList();
                                                                                        foreach (var c3 in ctorss)
                                                                                        {
                                                                                            var ps3 = c3.GetParameters();
                                                                                            var ctorArgs3 = ps3.Select(p => (object?)null).ToArray();
                                                                                            try { installerInstance3 = c3.Invoke(ctorArgs3); if (installerInstance3 != null) break; } catch { }
                                                                                        }
                                                                                    }
                                                                                    catch { }
                                                                                }

                                                                                if (installerInstance3 != null)
                                                                                {
                                                                                    try
                                                                                    {
                                                                                        var instField2 = gen2.GetType().GetField("_installer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                                                                        if (instField2 != null)
                                                                                        {
                                                                                            instField2.SetValue(gen2, installerInstance3);
                                                                                            FindNeedlePluginLib.Logger.Instance.Log($"Injected installer into retry generator instance: {installerInstance3.GetType().FullName}");
                                                                                        }
                                                                                    }
                                                                                    catch (Exception ie2)
                                                                                    {
                                                                                        FindNeedlePluginLib.Logger.Instance.Log($"Failed to set _installer on retry generator instance: {ie2.Message}");
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                        catch { }
                                                                    }
                                                                }
                                                                catch { }
                                                                try
                                                                {
                                                                    var genMethodRetry = genType.GetMethod("GenerateUML", new Type[] { typeof(string) });
                                                                    object? outputGeneratedRetry = null;
                                                                    if (genMethodRetry != null)
                                                                    {
                                                                        outputGeneratedRetry = genMethodRetry.Invoke(gen2, new object[] { outPath });
                                                                    }
                                                                    else
                                                                    {
                                                                        var methods = genType.GetMethods().Where(m => m.Name == "GenerateUML").ToList();
                                                                        if (methods.Count > 0)
                                                                        {
                                                                            var m = methods[0];
                                                                            var parameters = m.GetParameters();
                                                                            var args = new List<object?>();
                                                                            foreach (var p in parameters)
                                                                            {
                                                                                if (p.ParameterType == typeof(string)) args.Add(outPath); else args.Add(null);
                                                                            }
                                                                            outputGeneratedRetry = m.Invoke(gen2, args.ToArray());
                                                                        }
                                                                    }
                                                                    var genNameProp2 = genType.GetProperty("Name");
                                                                    var genName2 = genNameProp2?.GetValue(gen2)?.ToString() ?? genType.Name;
                                                                    FindNeedlePluginLib.Logger.Instance.Log($"UML generated by {genName2} (retry): {outputGeneratedRetry}");
                                                                }
                                                                catch (Exception rex)
                                                                {
                                                                    FindNeedlePluginLib.Logger.Instance.Log($"Retry failed invoking UML generator: {rex.Message}");
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            catch (Exception iex)
                                            {
                                                FindNeedlePluginLib.Logger.Instance.Log($"Installer retry failed: {iex.Message}");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        FindNeedlePluginLib.Logger.Instance.Log($"Failed to construct UML generator type: {genType.FullName}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                FindNeedlePluginLib.Logger.Instance.Log($"Failed to generate UML image: {ex.Message}");
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
