using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BasicTextPlugin;
using FindNeedlePluginLib;
using findneedle.Implementations;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Integration;

/// <summary>
/// End-to-end check of the "open a log → see results" path the UX drives: a real on-disk .log,
/// parsed by the real <see cref="PlainTextProcessor"/>, run through a full
/// <see cref="NuSearchQuery.RunThrough()"/>, with results read back from the result storage the
/// same way the viewer's paged source does. Forces InMemory storage so it's deterministic and
/// leaves no SQLite cache file behind. CI-runnable — no UI / no FlaUI.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class LogLoadEndToEndTests
{
    private string _dir = null!;
    private string _logPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"FN_e2e_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _logPath = Path.Combine(_dir, "sample.log");
        File.WriteAllLines(_logPath, new[]
        {
            "[2024-01-15 09:00:00] INFO: Application started successfully",
            "[2024-01-15 09:01:23] ERROR: Database connection failed - timeout after 30s",
            "[2024-01-15 09:02:15] WARNING: Memory usage at 85%",
            "",                       // blank line — the processor should skip it
            "[2024-01-15 09:03:45] ERROR: File not found: config.xml",
        });
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static FolderLocation LogLocation(string path)
    {
        var loc = new FolderLocation { path = path };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new PlainTextProcessor() });
        return loc;
    }

    [TestMethod]
    public void OpenLog_RunSearch_LoadsParsedResults()
    {
        var query = new NuSearchQuery { OverrideStorageType = StorageType.InMemory };
        query.Locations.Add(LogLocation(_logPath));

        query.RunThrough();

        var storage = query.ResultStorage;
        Assert.IsNotNull(storage, "the search should have created result storage");

        // Raw store = every parsed line. 4 non-blank lines (the empty line is skipped).
        var raw = new List<ISearchResult>();
        storage!.GetRawResultsInBatches(b => raw.AddRange(b), 1000);
        Assert.AreEqual(4, raw.Count, "every non-blank log line should load as a result");

        // Filtered store = what the viewer actually reads. With no filters configured, the rows
        // flow through to it.
        var filtered = new List<ISearchResult>();
        storage.GetFilteredResultsInBatches(b => filtered.AddRange(b), 1000);
        Assert.IsTrue(filtered.Count >= 1, "loaded results should reach the viewable (filtered) store");

        // Parsed correctly: level, message, and timestamp all came through.
        var dbError = raw.FirstOrDefault(r => r.GetMessage().Contains("Database connection failed"));
        Assert.IsNotNull(dbError, "the ERROR line should be present");
        Assert.AreEqual(Level.Error, dbError!.GetLevel());
        Assert.AreEqual(2024, dbError.GetLogTime().Year);

        Assert.AreEqual(2, raw.Count(r => r.GetLevel() == Level.Error), "two ERROR lines");
        Assert.AreEqual(1, raw.Count(r => r.GetLevel() == Level.Warning), "one WARNING line");
        Assert.AreEqual(1, raw.Count(r => r.GetLevel() == Level.Info), "one INFO line");
    }

    [TestMethod]
    public void OpenLog_AtLocationLevel_ParsesContent()
    {
        // Tighter check on the open+parse step the search relies on.
        var loc = LogLocation(_logPath);

        loc.LoadInMemory();
        var results = loc.Search();

        Assert.AreEqual(4, results.Count);
        Assert.IsTrue(
            results.Any(r => r.GetLevel() == Level.Error && r.GetMessage().Contains("config.xml")),
            "the second ERROR line should be parsed with Level.Error");
    }
}
