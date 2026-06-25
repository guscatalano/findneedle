using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using FindNeedleRuleDSL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleRuleDSLTests;

/// <summary>
/// Fresh-install sanity for the bundled CommonRules: every shipped *.rules.json must be valid JSON
/// that deserializes to the rule model, and every rule's match/unmatch must be a compilable regex.
/// A malformed default rule would otherwise only surface when a user's search happened to load it.
/// </summary>
[TestClass]
public class CommonRulesValidityTests
{
    private static string CommonRulesDir => Path.Combine(AppContext.BaseDirectory, "CommonRules");

    private static string[] RuleFiles =>
        Directory.Exists(CommonRulesDir)
            ? Directory.GetFiles(CommonRulesDir, "*.rules.json", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();

    [TestMethod]
    public void CommonRules_AreBundled()
    {
        Assert.IsTrue(RuleFiles.Length >= 10,
            $"expected the bundled CommonRules to be copied to {CommonRulesDir}, found {RuleFiles.Length}");
    }

    [TestMethod]
    public void CommonRules_EveryFileParsesAndRegexesCompile()
    {
        var failures = new List<string>();
        foreach (var file in RuleFiles)
        {
            var name = Path.GetFileName(file);
            UnifiedRuleSet? set;
            try
            {
                set = JsonSerializer.Deserialize<UnifiedRuleSet>(File.ReadAllText(file));
            }
            catch (Exception ex)
            {
                failures.Add($"{name}: JSON did not parse — {ex.Message}");
                continue;
            }
            if (set == null) { failures.Add($"{name}: deserialized to null"); continue; }
            if (set.Sections == null) { failures.Add($"{name}: no sections"); continue; }

            foreach (var section in set.Sections)
                foreach (var rule in section.Rules)
                {
                    TryCompile(failures, name, rule.Name, "match", rule.Match);
                    TryCompile(failures, name, rule.Name, "unmatch", rule.Unmatch);
                }
        }

        Assert.AreEqual(0, failures.Count,
            "Bundled CommonRules have problems:\n  " + string.Join("\n  ", failures));
    }

    private static void TryCompile(List<string> failures, string file, string ruleName, string field, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return;
        try { _ = new Regex(pattern); }
        catch (Exception ex) { failures.Add($"{file}: rule '{ruleName}' has invalid {field} regex — {ex.Message}"); }
    }
}
