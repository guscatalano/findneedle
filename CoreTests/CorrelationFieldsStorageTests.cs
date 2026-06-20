using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FindNeedleCoreUtils;
using FindNeedlePluginLib;
using FindPluginCore.Implementations.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// Verifies the ProcessId / ThreadId / ActivityId columns added to the SQLite store round-trip:
/// they're written on insert and read back via the batch reader and the by-id lookup.
/// </summary>
[TestClass]
[TestCategory("Storage")]
public class CorrelationFieldsStorageTests
{
    private readonly List<string> _paths = new();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var p in _paths) try { if (File.Exists(p)) File.Delete(p); } catch { }
    }

    private SqliteStorage NewSqlite()
    {
        var f = Path.Combine(Path.GetTempPath(), "corrtest_" + Guid.NewGuid().ToString("N"));
        _paths.Add(CachedStorage.GetCacheFilePath(f, ".db"));
        return new SqliteStorage(f);
    }

    [TestMethod]
    public void ProcessThreadActivity_RoundTrip()
    {
        using var s = NewSqlite();
        s.ClearTables();
        s.AddFilteredBatch(new List<ISearchResult>
        {
            new R("alpha") { Pid = "4120", Tid = "7880", Act = "{11111111-1111-1111-1111-111111111111}",
                Eid = "1003", Kw = "Audit Success", Rel = "{22222222-2222-2222-2222-222222222222}",
                Chan = "Application", PGuid = "{33333333-3333-3333-3333-333333333333}", Rec = "106936" },
            new R("beta")  { Pid = "", Tid = "", Act = "" }, // a source without correlation fields
        });

        var all = new List<ISearchResult>();
        s.GetFilteredResultsInBatches(b => all.AddRange(b));
        Assert.AreEqual(2, all.Count);

        var alpha = all.First(r => r.GetMessage() == "alpha");
        Assert.AreEqual("4120", alpha.GetProcessId());
        Assert.AreEqual("7880", alpha.GetThreadId());
        Assert.AreEqual("{11111111-1111-1111-1111-111111111111}", alpha.GetActivityId());
        Assert.AreEqual("1003", alpha.GetEventId());
        Assert.AreEqual("Audit Success", alpha.GetKeywords());
        Assert.AreEqual("{22222222-2222-2222-2222-222222222222}", alpha.GetRelatedActivityId());
        Assert.AreEqual("Application", alpha.GetChannel());
        Assert.AreEqual("{33333333-3333-3333-3333-333333333333}", alpha.GetProviderGuid());
        Assert.AreEqual("106936", alpha.GetRecordId());

        var beta = all.First(r => r.GetMessage() == "beta");
        Assert.AreEqual("", beta.GetProcessId());
        Assert.AreEqual("", beta.GetThreadId());

        // By-id lookup carries the fields too.
        var byId = s.GetById(alpha.GetRowId());
        Assert.IsNotNull(byId);
        Assert.AreEqual("4120", byId.GetProcessId());
        Assert.AreEqual("{11111111-1111-1111-1111-111111111111}", byId.GetActivityId());
    }

    private sealed class R : ISearchResult
    {
        private readonly string _m;
        public R(string m) { _m = m; }
        public string Pid = "", Tid = "", Act = "";
        public string Eid = "", Kw = "", Rel = "", Chan = "", PGuid = "", Rec = "";
        public DateTime GetLogTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "u";
        public string GetTaskName() => "t";
        public string GetOpCode() => "";
        public string GetSource() => "s";
        public string GetSearchableData() => _m;
        public string GetMessage() => _m;
        public string GetResultSource() => "rs";
        public string GetProcessId() => Pid;
        public string GetThreadId() => Tid;
        public string GetActivityId() => Act;
        public string GetEventId() => Eid;
        public string GetKeywords() => Kw;
        public string GetRelatedActivityId() => Rel;
        public string GetChannel() => Chan;
        public string GetProviderGuid() => PGuid;
        public string GetRecordId() => Rec;
    }
}
