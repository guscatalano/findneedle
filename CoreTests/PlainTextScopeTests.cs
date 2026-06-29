using System;
using System.IO;
using System.Linq;
using BasicTextPlugin;
using findneedle.Implementations;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// The decode-time triage scope (<see cref="DecodeScope"/>) extended to plain text. Text has no provider or
/// reliable level at decode time, so only the TIME WINDOW applies — and only to lines that actually parse a
/// timestamp; an un-timestamped line (e.g. a wrapped stack frame) is always kept since we can't classify it.
/// Mirrors the ETL/EVTX decode filter: out-of-window rows are never added to the result set.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PlainTextScopeTests
{
    [TestCleanup]
    public void Cleanup() => DecodeScope.Current = null; // don't leak the global scope to other tests

    private static string WriteLog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FN_txtscope_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "app.log");
        File.WriteAllLines(f, new[]
        {
            "2026-06-01 10:00:00 line A",
            "2026-06-01 11:00:00 line B",
            "2026-06-01 12:00:00 line C",
            "    continued frame with no timestamp",
        });
        return f;
    }

    private static System.Collections.Generic.List<string> Load(string f, DecodeScope scope)
    {
        DecodeScope.Current = scope;
        var proc = new PlainTextProcessor();
        proc.OpenFile(f);
        proc.LoadInMemory();
        return proc.GetResults().Select(r => r.GetMessage()).ToList();
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void Diag_ParsesBracketedTimestamp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FN_txtscope_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "b.log");
        File.WriteAllLines(f, new[] { "[2026-01-01 00:00:00] INFO: needle line 0", "[2026-02-01 00:00:00] INFO: needle line 1" });
        try
        {
            DecodeScope.Current = null;
            var p = new PlainTextProcessor();
            p.OpenFile(f);
            p.LoadInMemory();
            var r = p.GetResults();
            Assert.AreEqual(2, r.Count);
            Assert.AreEqual(new DateTime(2026, 1, 1, 0, 0, 0), r[0].GetLogTime(), "bracketed timestamp must parse (else scope can't filter)");
        }
        finally { TryDelete(f); }
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void NoScope_KeepsEveryLine()
    {
        var f = WriteLog();
        try { Assert.AreEqual(4, Load(f, null).Count); }
        finally { TryDelete(f); }
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void TimeWindowScope_DropsOutOfWindowLines_KeepsUntimestamped()
    {
        var f = WriteLog();
        try
        {
            // From 11:00 (local — text timestamps carry no tz, so the decoder treats them as local): line A
            // at 10:00 is before the window and must be dropped; B/C are in-window; the un-timestamped line
            // can't be classified, so it stays.
            var from = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Local).ToUniversalTime();
            var texts = Load(f, new DecodeScope { FromUtc = from });

            Assert.AreEqual(3, texts.Count, "line A (10:00) is before the window and must be dropped");
            Assert.IsFalse(texts.Any(t => t.Contains("line A")), "out-of-window line dropped at decode time");
            Assert.IsTrue(texts.Any(t => t.Contains("line B")));
            Assert.IsTrue(texts.Any(t => t.Contains("line C")));
            Assert.IsTrue(texts.Any(t => t.Contains("continued frame")), "un-timestamped line is always kept");
        }
        finally { TryDelete(f); }
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void ProviderOnlyScope_DoesNotDropText()
    {
        // A provider allow-list (e.g. carried over from an ETL triage in a mixed folder) must NOT nuke text:
        // plain text has no provider, so the provider dimension is skipped and every line survives.
        var f = WriteLog();
        try
        {
            var scope = new DecodeScope
            {
                IncludeProviders = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Some-ETW-Provider" }
            };
            Assert.AreEqual(4, Load(f, scope).Count, "provider-only scope leaves text untouched (no provider to match)");
        }
        finally { TryDelete(f); }
    }

    private static void TryDelete(string f)
    {
        DecodeScope.Current = null;
        try { Directory.Delete(Path.GetDirectoryName(f)!, true); } catch { /* best effort */ }
    }

    // Reproduce the full shipped path (rule file -> NuSearchQuery -> FolderLocation -> PlainTextProcessor)
    // cheaply, so a regression in the wiring is caught without the multi-million-line perf fixture.
    [TestMethod]
    [TestCategory("Storage")]
    public void FullPipeline_TimeWindowRule_FiltersTextLoad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FN_txtscope_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var log = Path.Combine(dir, "app.log");
        File.WriteAllLines(log, new[]
        {
            "[2026-01-01 00:00:00] INFO: line jan",
            "[2026-01-05 00:00:00] INFO: line jan2",
            "[2026-02-01 00:00:00] INFO: line feb",
            "[2026-02-20 00:00:00] INFO: line feb2",
        });
        var rules = Path.Combine(dir, "scope.rules.json");
        File.WriteAllText(rules,
            "{ \"sections\": [ { \"name\": \"scope\", \"purpose\": \"scope\", \"rules\": [ { \"name\": \"scope\", " +
            "\"action\": { \"type\": \"scope\", \"timeTo\": \"2026-01-15T00:00:00Z\" } } ] } ] }");
        try
        {
            FindPluginCore.Implementations.Storage.SqliteStorage.ParallelIngestEnabled = false;
            var loc = new FolderLocation { path = log };
            loc.SetExtensionProcessorList(new System.Collections.Generic.List<IFileExtensionProcessor> { new PlainTextProcessor() });
            var cacheDb = FindNeedleCoreUtils.CachedStorage.GetCacheFilePath(loc.GetName(), ".db");
            foreach (var p in new[] { cacheDb, cacheDb + "-wal", cacheDb + "-shm", cacheDb + "-journal" })
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            var q = new FindPluginCore.Searching.NuSearchQuery
            {
                OverrideStorageType = FindPluginCore.PluginSubsystem.StorageType.SqlLite,
                CacheReuseMode = FindPluginCore.Searching.CacheReuseMode.Never,
            };
            q.Locations.Add(loc);
            q.RulesConfigPaths.Add(rules);
            q.RunThrough();
            long rows = ((FindPluginCore.Implementations.Storage.SqliteStorage)q.ResultStorage!).GetStatistics().filteredRecordCount;
            q.DisposeStorage();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

            // Only the two January lines are <= the 2026-01-15 window (the tz shift is hours, not weeks).
            Assert.AreEqual(2, rows, "the time-window scope rule must filter the text load through the full pipeline");
        }
        finally { DecodeScope.Current = null; try { Directory.Delete(dir, true); } catch { } }
    }
}
