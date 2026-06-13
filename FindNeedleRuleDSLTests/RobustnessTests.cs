using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FindNeedlePluginLib;
using FindNeedlePluginLib.TestClasses;
using FindNeedleRuleDSL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleRuleDSLTests;

/// <summary>
/// Robustness tests for the rule DSL: malformed input, invalid regex, missing files,
/// and pathological patterns. Covers TESTING_PLAN.md R-01..R-04, R-12.
///
/// Pins the current "silent fallback" behaviour of <see cref="UnifiedRuleProcessor"/>
/// (regex → substring on exception) and <see cref="FindNeedleRuleDSLPlugin"/>
/// (catch + Debug.WriteLine on load failures) so refactors can't quietly remove it.
/// </summary>
[TestClass]
[TestCategory("Robustness")]
public class RobustnessTests
{
    private string? _tempRulesFile;

    [TestCleanup]
    public void Cleanup()
    {
        if (_tempRulesFile != null && File.Exists(_tempRulesFile))
        {
            try { File.Delete(_tempRulesFile); } catch { }
        }
    }

    private string CreateTempRulesFile(string contents)
    {
        var dir = Path.Combine(Path.GetTempPath(), "FindNeedleRuleDSLTests_Robustness");
        Directory.CreateDirectory(dir);
        _tempRulesFile = Path.Combine(dir, $"rules-{Guid.NewGuid()}.json");
        File.WriteAllText(_tempRulesFile, contents);
        return _tempRulesFile;
    }

    private static UnifiedRuleSet RuleSetWith(string match, string? unmatch = null)
    {
        return new UnifiedRuleSet
        {
            Title = "Robustness Test",
            Sections = new List<UnifiedRuleSection>
            {
                new UnifiedRuleSection
                {
                    Name = "Section",
                    Providers = new List<string> { "TestProvider" },
                    Rules = new List<UnifiedRule>
                    {
                        new UnifiedRule
                        {
                            Name = "Rule",
                            Match = match,
                            Unmatch = unmatch,
                            Enabled = true,
                            Action = new UnifiedRuleAction { Type = "tag", Tag = "T" }
                        }
                    }
                }
            }
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R-01: Invalid Match regex falls back to substring match.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void R01_InvalidMatchRegex_FallsBackToSubstring()
    {
        // "[unclosed" is not a valid regex (unclosed character class).
        // Current behaviour: catch the RegexParseException and substring-match instead.
        var ruleSet = RuleSetWith(match: "[unclosed");
        var processor = new UnifiedRuleProcessor(ruleSet, "TestProvider");
        var results = new List<object> { "this line literally contains [unclosed somewhere" };

        var matches = processor.Process(results, o => o.ToString() ?? string.Empty).ToList();

        Assert.AreEqual(1, matches.Count,
            "Invalid regex should silently fall back to substring matching, not throw.");
    }

    [TestMethod]
    public void R01_InvalidMatchRegex_NonMatchingSubstring_ReturnsNoMatch()
    {
        // The fallback is substring, not "everything matches" — verify the negative case.
        var ruleSet = RuleSetWith(match: "[unclosed");
        var processor = new UnifiedRuleProcessor(ruleSet, "TestProvider");
        var results = new List<object> { "this line does not contain the literal bracket text" };

        var matches = processor.Process(results, o => o.ToString() ?? string.Empty).ToList();

        Assert.AreEqual(0, matches.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R-12: Invalid Unmatch regex falls back to substring (mirror of R-01).
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void R12_InvalidUnmatchRegex_FallsBackToSubstring_ExcludesResult()
    {
        // Match passes (valid regex "error"), Unmatch is invalid → fallback to substring,
        // which finds "[unclosed" in the data → result is excluded.
        var ruleSet = RuleSetWith(match: "error", unmatch: "[unclosed");
        var processor = new UnifiedRuleProcessor(ruleSet, "TestProvider");
        var results = new List<object> { "this is an error message with [unclosed too" };

        var matches = processor.Process(results, o => o.ToString() ?? string.Empty).ToList();

        Assert.AreEqual(0, matches.Count,
            "Invalid unmatch regex should substring-match and exclude the result.");
    }

    [TestMethod]
    public void R12_InvalidUnmatchRegex_SubstringAbsent_IncludesResult()
    {
        var ruleSet = RuleSetWith(match: "error", unmatch: "[unclosed");
        var processor = new UnifiedRuleProcessor(ruleSet, "TestProvider");
        var results = new List<object> { "this is an error message without the literal bracket" };

        var matches = processor.Process(results, o => o.ToString() ?? string.Empty).ToList();

        Assert.AreEqual(1, matches.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R-02: Pathological (catastrophic-backtracking) regex completes under
    // a strict perf budget. Today this is a documented DoS surface — the
    // processor calls Regex.IsMatch without a timeout. The test enforces the
    // budget via Task + Wait; if it ever exceeds, we surface the regression.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void R02_CatastrophicBacktracking_CompletesWithinBudget()
    {
        // Classic exponential-backtracking pattern on a near-matching input.
        // Without a Regex timeout, (a+)+$ against "aaaaa...!" backtracks for seconds.
        var ruleSet = RuleSetWith(match: "^(a+)+$");
        var processor = new UnifiedRuleProcessor(ruleSet, "TestProvider");
        var pathological = new string('a', 30) + "!"; // tuned to take ms today, seconds without protection
        var results = new List<object> { pathological };

        // 2-second budget. Default .NET regex has no global timeout; rely on the test
        // host process to exit even if a runaway thread persists past timeout.
        var task = Task.Run(() => processor.Process(results, o => o.ToString() ?? string.Empty).ToList());
        var completed = task.Wait(TimeSpan.FromSeconds(2));

        Assert.IsTrue(completed,
            "Pathological regex exceeded 2s budget. UnifiedRuleProcessor.Process should " +
            "pass a Regex timeout (RegexOptions + TimeSpan overload) to bound worst-case work.");
        Assert.AreEqual(0, task.Result.Count,
            "Pattern intentionally does not match — assert the boundary behaviour is correct too.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R-03: Malformed JSON in the rules file is swallowed; processor reports
    // zero matches rather than throwing into the caller.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void R03_MalformedJsonRulesFile_DoesNotThrow_AndProducesNoTags()
    {
        var malformed = "{ \"title\": \"oops\", \"sections\": [ { \"name\": "; // truncated, invalid JSON
        var path = CreateTempRulesFile(malformed);

        var plugin = new FindNeedleRuleDSLPlugin("TestProvider", path);
        var results = new List<ISearchResult> { new FakeSearchResult { searchableDataString = "anything" } };

        // Must not throw — current code catches and Debug.WriteLines.
        plugin.ProcessResults(results);

        Assert.AreEqual(0, plugin.GetFoundTags().Count(),
            "Malformed rules JSON must not produce phantom matches.");
        Assert.AreEqual(0, plugin.GetMatchedResults().Count());
    }

    [TestMethod]
    public void R03_EmptyRulesFile_DoesNotThrow()
    {
        var path = CreateTempRulesFile(string.Empty);

        var plugin = new FindNeedleRuleDSLPlugin("TestProvider", path);
        var results = new List<ISearchResult> { new FakeSearchResult { searchableDataString = "x" } };

        plugin.ProcessResults(results);

        Assert.AreEqual(0, plugin.GetFoundTags().Count());
    }

    [TestMethod]
    public void R03_RulesFileWithNullSections_DoesNotThrow()
    {
        // Valid JSON, but sections array is missing entirely. UnifiedRuleSet.Sections
        // defaults to empty list — exercise that path explicitly.
        var path = CreateTempRulesFile(@"{ ""title"": ""no sections"" }");

        var plugin = new FindNeedleRuleDSLPlugin("TestProvider", path);
        var results = new List<ISearchResult> { new FakeSearchResult { searchableDataString = "x" } };

        plugin.ProcessResults(results);

        Assert.AreEqual(0, plugin.GetFoundTags().Count());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R-04: Missing rules file is handled gracefully.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void R04_RulesFileDoesNotExist_DoesNotThrow_ZeroMatches()
    {
        var bogusPath = Path.Combine(Path.GetTempPath(), $"definitely-does-not-exist-{Guid.NewGuid()}.json");
        Assert.IsFalse(File.Exists(bogusPath), "Test precondition: path must not exist.");

        var plugin = new FindNeedleRuleDSLPlugin("TestProvider", bogusPath);
        var results = new List<ISearchResult> { new FakeSearchResult { searchableDataString = "anything" } };

        plugin.ProcessResults(results);

        Assert.AreEqual(0, plugin.GetFoundTags().Count());
        Assert.AreEqual(0, plugin.GetMatchedResults().Count());
    }

    [TestMethod]
    public void R04_RulesFilePathPointsToDirectory_DoesNotThrow()
    {
        // Path resolves but is not a file. Today File.Exists returns false for a directory
        // path, so the plugin should treat it as missing.
        var dir = Path.Combine(Path.GetTempPath(), $"rules-dir-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            var plugin = new FindNeedleRuleDSLPlugin("TestProvider", dir);
            var results = new List<ISearchResult> { new FakeSearchResult { searchableDataString = "x" } };

            plugin.ProcessResults(results);

            Assert.AreEqual(0, plugin.GetFoundTags().Count());
        }
        finally
        {
            try { Directory.Delete(dir); } catch { }
        }
    }
}
