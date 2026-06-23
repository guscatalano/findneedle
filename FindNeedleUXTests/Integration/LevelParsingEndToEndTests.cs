using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicTextPlugin;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using findneedle.Implementations;
using FindNeedleUX.Services.PagedLogSource;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Integration;

/// <summary>
/// Generates a synthetic log that exercises EVERY level the plain-text parser can produce
/// (CRITICAL/FATAL→Catastrophic, ERROR→Error, WARNING/WARN→Warning, DEBUG/VERBOSE→Verbose, else Info)
/// and verifies the search pipeline parses + counts each one. Guards level parsing and the per-level
/// counts that feed the viewer's level chips. CI-runnable (synthetic log, SQLite backend).
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class LevelParsingEndToEndTests
{
    // Distinct counts per level so a miscount is unambiguous.
    private const int Catastrophic = 2, Error = 3, Warning = 4, Verbose = 5, Info = 6;
    private const int Total = Catastrophic + Error + Warning + Verbose + Info; // 20

    private string _dir = null!;
    private string _logPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"FN_levels_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _logPath = Path.Combine(_dir, "levels.log");

        var lines = new List<string>(Total);
        // Innocuous payloads so the only level keyword on a line is the intended one.
        for (int i = 0; i < Catastrophic; i++) lines.Add($"[2024-03-01 10:00:00] CRITICAL: subsystem down {i}");
        for (int i = 0; i < Error; i++)        lines.Add($"[2024-03-01 10:00:00] ERROR: operation rejected {i}");
        for (int i = 0; i < Warning; i++)      lines.Add($"[2024-03-01 10:00:00] WARNING: nearing limit {i}");
        for (int i = 0; i < Verbose; i++)      lines.Add($"[2024-03-01 10:00:00] DEBUG: state dump {i}");
        for (int i = 0; i < Info; i++)         lines.Add($"[2024-03-01 10:00:00] checkpoint reached {i}"); // no keyword → Info
        File.WriteAllLines(_logPath, lines);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static NuSearchQuery RunSqliteSearch(string path)
    {
        var loc = new FolderLocation { path = path };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new PlainTextProcessor() });
        var query = new NuSearchQuery { OverrideStorageType = StorageType.SqlLite };
        query.Locations.Add(loc);
        query.RunThrough();
        return query;
    }

    [TestMethod]
    public void AllLevels_ParseAndCount()
    {
        var query = RunSqliteSearch(_logPath);
        try
        {
            using var source = PagedLogSourceFactory.Create(query.ResultStorage!, fallbackInMemory: null);

            Assert.AreEqual(Total, source.GetFilteredCount(FilterSpec.Empty), "every line should load");

            int CountLevel(string level) =>
                source.GetFilteredCount(new FilterSpec("", "", "", "", "", level, null, null));

            Assert.AreEqual(Catastrophic, CountLevel("Catastrophic"), "CRITICAL → Catastrophic");
            Assert.AreEqual(Error, CountLevel("Error"), "ERROR → Error");
            Assert.AreEqual(Warning, CountLevel("Warning"), "WARNING → Warning");
            Assert.AreEqual(Verbose, CountLevel("Verbose"), "DEBUG → Verbose");
            Assert.AreEqual(Info, CountLevel("Info"), "no keyword → Info");

            // The level chips read distinct levels — all five should be present.
            var distinct = source.GetDistinctLevels();
            Assert.AreEqual(5, distinct.Count, $"expected 5 distinct levels, got: {string.Join(", ", distinct)}");

            // Per-level counts (the chip numbers) should cover every row exactly once.
            var levelCounts = source.GetLevelCounts(FilterSpec.Empty);
            Assert.AreEqual(Total, levelCounts.Values.Sum(), "level counts cover every row");
        }
        finally
        {
            query.DisposeStorage();
        }
    }
}
