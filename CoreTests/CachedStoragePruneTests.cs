using System;
using System.IO;
using FindNeedleCoreUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>Cache eviction: the result-cache directory had no ceiling and grew into the hundreds of GB.
/// <see cref="CachedStorage.PruneDirectory"/> evicts least-recently-written files until under the cap.</summary>
[TestClass]
public class CachedStoragePruneTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fn_prunetest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup() { try { Directory.Delete(_dir, true); } catch { } }

    private string MakeFile(string name, int bytes, DateTime mtimeUtc)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllBytes(p, new byte[bytes]);
        File.SetLastWriteTimeUtc(p, mtimeUtc);
        return p;
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void PruneDirectory_ShardAware_EvictsShardsWithDb_AndSweepsOrphans()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Oldest cache entry: a.db (500) + two shard files (500 each) = 1500.
        var aDb = MakeFile("a.db", 500, t);
        var aS0 = MakeFile("a.db.fts0", 500, t);
        var aS1 = MakeFile("a.db.fts1", 500, t);
        // Newer entry: b.db (500) + one shard (500) = 1000.
        var bDb = MakeFile("b.db", 500, t.AddHours(2));
        var bS0 = MakeFile("b.db.fts0", 500, t.AddHours(2));
        // Orphan shard: its base (gone.db) doesn't exist → should be swept regardless of cap.
        var orphan = MakeFile("gone.db.fts0", 400, t.AddHours(1));

        // Total real-entry bytes = 1500 + 1000 = 2500 (orphan excluded). Cap 1200 → evict the oldest
        // entry (a.db + its 2 shards = 1500). Orphan is swept too.
        long freed = CachedStorage.PruneDirectory(_dir, maxBytes: 1200);

        Assert.IsFalse(File.Exists(aDb), "oldest .db evicted");
        Assert.IsFalse(File.Exists(aS0), "its shard evicted with the .db");
        Assert.IsFalse(File.Exists(aS1), "its shard evicted with the .db");
        Assert.IsFalse(File.Exists(orphan), "orphan shard (no base .db) swept");
        Assert.IsTrue(File.Exists(bDb), "newer entry kept");
        Assert.IsTrue(File.Exists(bS0), "newer entry's shard kept");
        Assert.AreEqual(1500 + 400, freed, "freed = evicted entry (1500) + orphan (400)");
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void PruneDirectory_EvictsOldestFirst_UntilUnderCap()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var oldest = MakeFile("a.db", 1000, t);
        var mid = MakeFile("b.db", 1000, t.AddHours(1));
        var newest = MakeFile("c.db", 1000, t.AddHours(2));

        // 3000 bytes total, cap 2500 → evict just the oldest (1000) to drop under.
        long freed = CachedStorage.PruneDirectory(_dir, maxBytes: 2500);

        Assert.AreEqual(1000, freed);
        Assert.IsFalse(File.Exists(oldest), "oldest-written file should be evicted first");
        Assert.IsTrue(File.Exists(mid));
        Assert.IsTrue(File.Exists(newest));
        var (files, bytes) = CachedStorage.GetStats(_dir);
        Assert.AreEqual(2, files);
        Assert.AreEqual(2000, bytes);
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void PruneDirectory_UnderCap_IsNoOp()
    {
        MakeFile("a.db", 1000, DateTime.UtcNow);
        Assert.AreEqual(0, CachedStorage.PruneDirectory(_dir, maxBytes: 1_000_000));
        Assert.AreEqual(1, CachedStorage.GetStats(_dir).files);
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void PruneDirectory_HonorsFileCountCap()
    {
        var t = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 5; i++) MakeFile($"f{i}.db", 100, t.AddMinutes(i));
        // Keep at most 2 files (by count) regardless of size.
        CachedStorage.PruneDirectory(_dir, maxBytes: long.MaxValue, maxFiles: 2);
        Assert.AreEqual(2, CachedStorage.GetStats(_dir).files);
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "f3.db")) && File.Exists(Path.Combine(_dir, "f4.db")),
            "the two newest should survive the count cap");
    }
}
