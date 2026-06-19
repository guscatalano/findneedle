using System;
using System.Collections.Generic;
using System.IO;
using BasicTextPlugin;
using FindNeedlePluginLib;
using findneedle.Implementations;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Integration;

/// <summary>
/// Regression for "open several large files in a row / a folder of large files." Drives the same
/// load→search→store pipeline the Quick-open path uses (NuSearchQuery.RunThrough over a real
/// FolderLocation), forcing SQLite storage (the backend large/streaming loads use) so cross-file
/// state bugs surface. Each file must load its OWN rows — not zero, not a previous file's count.
/// CI-runnable: synthetic logs, no UI.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class MultiFileLoadEndToEndTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"FN_multi_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string WriteLog(string name, string marker, int lines)
    {
        var path = Path.Combine(_dir, name);
        using var w = new StreamWriter(path);
        for (int i = 1; i <= lines; i++)
            w.WriteLine($"[2024-01-15 09:00:00] INFO: {marker} event {i}");
        return path;
    }

    private static FolderLocation LogLocation(string path)
    {
        var loc = new FolderLocation { path = path };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new PlainTextProcessor() });
        return loc;
    }

    /// <summary>Run a fresh quick-load over a path (file or folder) and return (rawCount, filteredCount, firstMessage).</summary>
    private static (int raw, int filtered, string firstMessage) Load(string path)
    {
        var query = new NuSearchQuery
        {
            OverrideStorageType = StorageType.SqlLite,   // backend large/streaming loads actually use
            CacheReuseMode = CacheReuseMode.Never,       // always rescan — deterministic, no cache bleed
        };
        query.Locations.Add(LogLocation(path));
        query.RunThrough();

        var storage = query.ResultStorage;
        Assert.IsNotNull(storage, "search should create result storage");
        try
        {
            var stats = storage!.GetStatistics();
            string first = null!;
            storage.GetFilteredResultsInBatches(b => { if (first == null && b.Count > 0) first = b[0].GetMessage(); }, 1000);
            return (stats.rawRecordCount, stats.filteredRecordCount, first);
        }
        finally { try { storage!.Dispose(); } catch { } }
    }

    [TestMethod]
    public void SequentialDifferentFiles_EachLoadsItsOwnRows()
    {
        var a = WriteLog("alpha.log", "ALPHA", 20_000);
        var b = WriteLog("bravo.log", "BRAVO", 45_000);
        var c = WriteLog("charlie.log", "CHARLIE", 60_000);

        var ra = Load(a);
        Assert.AreEqual(20_000, ra.raw, "file A raw rows");
        Assert.AreEqual(20_000, ra.filtered, "file A filtered (viewer) rows");
        StringAssert.Contains(ra.firstMessage ?? "", "ALPHA", "file A content");

        var rb = Load(b);
        Assert.AreEqual(45_000, rb.raw, "file B raw rows");
        Assert.AreEqual(45_000, rb.filtered, "file B filtered (viewer) rows");
        StringAssert.Contains(rb.firstMessage ?? "", "BRAVO", "file B content");

        var rc = Load(c);
        Assert.AreEqual(60_000, rc.raw, "file C raw rows");
        Assert.AreEqual(60_000, rc.filtered, "file C filtered (viewer) rows");
        StringAssert.Contains(rc.firstMessage ?? "", "CHARLIE", "file C content");

        // Reopen the first file — must still load its own rows (no stale state from B/C).
        var ra2 = Load(a);
        Assert.AreEqual(20_000, ra2.filtered, "reopened file A filtered rows");
        StringAssert.Contains(ra2.firstMessage ?? "", "ALPHA", "reopened file A content");
    }

    [TestMethod]
    public void FolderWithMultipleLargeFiles_LoadsEveryFile()
    {
        WriteLog("f1.log", "ONE", 15_000);
        WriteLog("f2.log", "TWO", 25_000);
        WriteLog("f3.log", "THREE", 35_000);

        var r = Load(_dir); // a folder containing all three
        Assert.AreEqual(75_000, r.raw, "all files in the folder should load (sum of every file's lines)");
        Assert.AreEqual(75_000, r.filtered, "all loaded rows should reach the viewer store");
    }
}
