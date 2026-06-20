using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FindNeedlePluginLib;
using FindNeedlePluginLib.TestClasses;
using FindNeedleRuleDSL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleRuleDSLTests;

/// <summary>
/// Hand-rolled snapshot/regression tests for the example rule files in
/// <c>FindNeedleRuleDSL/Examples/</c>. Covers TESTING_PLAN.md R-13.
///
/// For each <c>*.rules.json</c> example, runs <see cref="FindNeedleRuleDSLPlugin"/>
/// against <c>TestData/sample-errors.log</c> with a sweep of common providers,
/// captures matched count + tag histogram, and compares against a checked-in
/// golden file under <c>TestData/snapshots/</c>.
///
/// First run with no golden file: writes the baseline to the source tree and
/// fails with "regenerated baseline; review the diff and re-run". This forces
/// a deliberate review step before any snapshot is committed.
///
/// To regenerate after a deliberate behaviour change:
///   1) Delete the affected <c>.snapshot.json</c> file under
///      <c>FindNeedleRuleDSLTests/TestData/snapshots/</c>.
///   2) Re-run the tests — the baseline is rewritten.
///   3) Inspect the diff (<c>git diff TestData/snapshots/</c>); commit if correct.
/// </summary>
[TestClass]
[TestCategory("Snapshot")]
public class ExampleSnapshotTests
{
    private static readonly string[] ProviderSweep = { "*", "EventLog", "ETW" };

    private static string _examplesDir = null!;
    private static string _sampleLogPath = null!;
    private static string _snapshotsDir = null!;
    private static List<ISearchResult> _logResults = null!;

    [ClassInitialize]
    public static void ClassSetup(TestContext _)
    {
        _examplesDir = LocateRepoFolder("FindNeedleRuleDSL", "Examples");
        _sampleLogPath = Path.Combine(LocateRepoFolder("FindNeedleRuleDSLTests", "TestData"), "sample-errors.log");
        _snapshotsDir = Path.Combine(LocateRepoFolder("FindNeedleRuleDSLTests", "TestData"), "snapshots");

        Directory.CreateDirectory(_snapshotsDir);

        if (!File.Exists(_sampleLogPath))
            throw new FileNotFoundException($"sample log not found: {_sampleLogPath}");

        _logResults = File.ReadAllLines(_sampleLogPath)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => (ISearchResult)new FakeSearchResult { searchableDataString = l })
            .ToList();
    }

    public static IEnumerable<object[]> ExampleRuleFiles()
    {
        // Resolve at enumeration time — ClassInitialize hasn't run yet when
        // MSTest first calls this for [DataTestMethod] expansion.
        var dir = LocateRepoFolder("FindNeedleRuleDSL", "Examples");
        // Most files use the *.rules.json convention; sample-rules.json is the one outlier.
        // Enumerate all .json so the suite covers every rule example in the folder.
        return Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
            .Select(p => new object[] { Path.GetFileName(p) });
    }

    /// <summary>
    /// One snapshot per example file. Display name is the filename so a failure
    /// in the test pane reads "Snapshot — crash-detection.rules.json".
    /// </summary>
    [DataTestMethod]
    [DynamicData(nameof(ExampleRuleFiles), DynamicDataSourceType.Method,
        DynamicDataDisplayName = nameof(SnapshotDisplayName))]
    public void Snapshot(string ruleFileName)
    {
        var rulePath = Path.Combine(_examplesDir, ruleFileName);
        Assert.IsTrue(File.Exists(rulePath), $"rule file missing: {rulePath}");

        // Build deterministic snapshot by sweeping providers + capturing tag histograms.
        var providersBlock = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var provider in ProviderSweep)
        {
            var plugin = new FindNeedleRuleDSLPlugin(provider, rulePath);
            plugin.ProcessResults(_logResults);

            var tags = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var tag in plugin.GetFoundTags())
                tags[tag] = plugin.GetTagCount(tag);

            providersBlock[provider] = new
            {
                matchedResults = plugin.GetMatchedResults().Count(),
                tags
            };
        }

        var snapshot = new
        {
            ruleFile = ruleFileName,
            logFile = Path.GetFileName(_sampleLogPath),
            providersSweep = ProviderSweep,
            providers = providersBlock
        };

        var actualJson = JsonSerializer.Serialize(snapshot,
            new JsonSerializerOptions { WriteIndented = true });

        var goldenPath = Path.Combine(_snapshotsDir, $"{ruleFileName}.snapshot.json");

        if (!File.Exists(goldenPath))
        {
            File.WriteAllText(goldenPath, actualJson);
            Assert.Fail(
                $"No baseline at {goldenPath}. Wrote initial snapshot. " +
                "Review the diff (`git diff TestData/snapshots/`), commit if correct, then re-run.");
        }

        var goldenJson = File.ReadAllText(goldenPath);
        if (!string.Equals(NormalizeNewlines(goldenJson), NormalizeNewlines(actualJson), StringComparison.Ordinal))
        {
            // Persist the actual output next to the golden so the dev can diff in their editor.
            var actualPath = goldenPath + ".actual";
            File.WriteAllText(actualPath, actualJson);
            Assert.Fail(
                $"Snapshot mismatch for {ruleFileName}.\n" +
                $"  golden: {goldenPath}\n" +
                $"  actual: {actualPath}\n" +
                "Diff them. If the new output is correct, delete the golden and re-run to regenerate.");
        }
    }

    public static string SnapshotDisplayName(System.Reflection.MethodInfo _, object[] data)
        => $"Snapshot — {data[0]}";

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static string LocateRepoFolder(params string[] segments)
    {
        // Prefer the source tree (so snapshot updates land in the repo for `git diff`)...
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        // ...otherwise fall back to the copy deployed next to the test assembly (Examples\, TestData\),
        // so tests run against built artifacts (CI's test-publish job) still find their data.
        var deployed = Path.Combine(AppContext.BaseDirectory, segments[segments.Length - 1]);
        if (Directory.Exists(deployed))
            return deployed;

        throw new DirectoryNotFoundException(
            $"Could not locate '{Path.Combine(segments)}' in the source tree or as a deployed copy ({deployed}), starting from {AppContext.BaseDirectory}");
    }

    private static string NormalizeNewlines(string s) => s.Replace("\r\n", "\n");
}
