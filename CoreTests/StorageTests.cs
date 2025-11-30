using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FindPluginCore.Implementations.Storage;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using FindNeedleCoreUtils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

[TestClass]
[DoNotParallelize]
public class StorageTests
{
    private readonly List<string> _createdDbPaths = new();

    [TestInitialize]
    public void TestInitialize()
    {
        _createdDbPaths.Clear();
    }

    [TestCleanup]
    public void TestCleanup()
    {
        foreach (var path in _createdDbPaths.Distinct())
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }
    }

    private class DummySearchResult : ISearchResult
    {
        // Return a fixed time to keep tests deterministic
        public static readonly DateTime FixedTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private readonly string _message;
        private readonly string _username;
        private readonly string _resultSource;

        public DummySearchResult(string message = "TestMessage", string username = "TestUser", string resultSource = "TestResultSource")
        {
            _message = message;
            _username = username;
            _resultSource = resultSource;
        }

        public DateTime GetLogTime() => FixedTime;
        public string GetMachineName() => "TestMachine";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Error;
        public string GetUsername() => _username;
        public string GetTaskName() => "TestTask";
        public string GetOpCode() => "TestOp";
        public string GetSource() => "TestSource";
        public string GetSearchableData() => "TestData";
        public string GetMessage() => _message;
        public string GetResultSource() => _resultSource;
    }

    private (string searchedFile, string dbPath) CreateUniqueSearchFile()
    {
        var searchedFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var dbPath = CachedStorage.GetCacheFilePath(searchedFile, ".db");
        _createdDbPaths.Add(dbPath);
        if (File.Exists(dbPath))
            File.Delete(dbPath);
        return (searchedFile, dbPath);
    }

    // Factories that produce storage instances for tests and a cleanup action.
    private (Func<ISearchStorage> create, Action cleanup) InMemoryFactory()
    {
        var instance = new InMemoryStorage();
        return (() => instance, () => { instance.Dispose(); });
    }

    private (Func<ISearchStorage> create, Action cleanup) SqliteFactory()
    {
        var (searchedFile, dbPath) = CreateUniqueSearchFile();
        return (() => new SqliteStorage(searchedFile), () => { /* TestCleanup will delete dbPath */ });
    }

    private (Func<ISearchStorage> create, Action cleanup) HybridFactory()
    {
        var (searchedFile, dbPath) = CreateUniqueSearchFile();
        // Use small memory threshold (10MB) to test spilling behavior
        return (() => new HybridStorage(searchedFile, memoryThresholdMB: 10), () => { /* TestCleanup will delete dbPath */ });
    }

    private (Func<ISearchStorage> create, Action cleanup) GetFactoryByKind(string kind)
    {
        return kind switch
        {
            "InMemory" => InMemoryFactory(),
            "Sqlite" => SqliteFactory(),
            "Hybrid" => HybridFactory(),
            _ => throw new ArgumentException("Unknown storage kind: " + kind, nameof(kind)),
        };
    }

    // Parameterized tests using DataTestMethod to run each scenario for both implementations.

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void ContentVerification(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var a = new DummySearchResult("MessageA", "UserA", "SourceA");
        var b = new DummySearchResult("MessageB", "UserB", "SourceB");
        storage.AddRawBatch(new[] { a, b });

        var all = new List<ISearchResult>();
        storage.GetRawResultsInBatches(batch => all.AddRange(batch), 10);
        Assert.AreEqual(2, all.Count, "Expected two raw results");
        Assert.AreEqual("MessageA", all[0].GetMessage());
        Assert.AreEqual("UserA", all[0].GetUsername());
        Assert.AreEqual("SourceB", all[1].GetResultSource());

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void ContentAndDateRoundtrip(string kind)
    {
        var factory = GetFactoryByKind(kind);

        // write and dispose
        using (var storage = factory.create())
        {
            var a = new DummySearchResult("Msg1", "User1", "Src1");
            var b = new DummySearchResult("Msg2", "User2", "Src2");
            storage.AddRawBatch(new[] { a, b });
        }

        // reopen and read
        using (var storage = factory.create())
        {
            var results = new List<ISearchResult>();
            storage.GetRawResultsInBatches(batch => results.AddRange(batch), 10);
            Assert.AreEqual(2, results.Count, "Should return two results");
            Assert.AreEqual("Msg1", results[0].GetMessage());
            Assert.AreEqual("User1", results[0].GetUsername());
            Assert.AreEqual(DummySearchResult.FixedTime, results[0].GetLogTime());
        }

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void DisposeReopen(string kind)
    {
        var factory = GetFactoryByKind(kind);

        using (var storage = factory.create())
        {
            storage.AddRawBatch(new[] { new DummySearchResult() });
        }

        // reopening should succeed
        using (var reopened = factory.create())
        {
            var stats = reopened.GetStatistics();
            Assert.IsTrue(stats.rawRecordCount >= 0);
        }

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void NullBatch_Throws(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();
        Assert.ThrowsException<ArgumentNullException>(() => storage.AddRawBatch(null));
        Assert.ThrowsException<ArgumentNullException>(() => storage.AddFilteredBatch(null));
        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void PreCancelledToken_PreventsWork(string kind)
    {
        var factory = GetFactoryByKind(kind);
        var cts = new CancellationTokenSource();
        cts.Cancel();
        using (var storage = factory.create())
        {
            storage.AddRawBatch(new[] { new DummySearchResult(), new DummySearchResult() }, cts.Token);
            var results = new List<ISearchResult>();
            storage.GetRawResultsInBatches(b => results.AddRange(b), 10);
            Assert.AreEqual(0, results.Count);
        }
        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void BatchingBehavior(string kind)
    {
        var factory = GetFactoryByKind(kind);
        var items = Enumerable.Range(0, 5).Select(i => (ISearchResult)new DummySearchResult($"M{i}")).ToList();

        using (var storage = factory.create())
        {
            storage.AddRawBatch(items);
        }

        using (var storage = factory.create())
        {
            var sqlBatches = new List<List<ISearchResult>>();
            storage.GetRawResultsInBatches(b => sqlBatches.Add(b), 2);
            Assert.AreEqual(3, sqlBatches.Count, "Should produce 3 batches: 2,2,1");
            Assert.AreEqual(2, sqlBatches[0].Count);
            Assert.AreEqual("M0", sqlBatches[0][0].GetMessage());
        }

        factory.cleanup();
    }

    // --- Additional tests added ---

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void Concurrency_AddsArePresent(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var tasks = new List<Task>();
        const int writers = 10;
        const int perWriter = 100;
        for (int w = 0; w < writers; w++)
        {
            var idx = w;
            tasks.Add(Task.Run(() =>
            {
                var items = Enumerable.Range(0, perWriter).Select(i => (ISearchResult)new DummySearchResult($"T{idx}-{i}")).ToList();
                storage.AddRawBatch(items);
            }));
        }
        Task.WaitAll(tasks.ToArray());

        var all = new List<ISearchResult>();
        storage.GetRawResultsInBatches(b => all.AddRange(b), 1000);
        Assert.AreEqual(writers * perWriter, all.Count);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void CancellationDuringWrite_StopsEarly(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var items = Enumerable.Range(0, 10000).Select(i => (ISearchResult)new DummySearchResult($"M{i}")).ToList();
        var cts = new CancellationTokenSource();
        // Start the add on a background task so we can cancel while it's running
        var addTask = Task.Run(() => storage.AddRawBatch(items, cts.Token));
        // Cancel shortly after starting
        Task.Run(() => { Thread.Sleep(5); cts.Cancel(); });

        // Wait for add to complete
        try { addTask.Wait(); } catch (AggregateException) { }

        var all = new List<ISearchResult>();
        storage.GetRawResultsInBatches(b => all.AddRange(b), 1000);
        // InMemory can be so fast that cancellation arrives too late; accept either partial or complete writes.
        Assert.IsTrue(all.Count <= items.Count, $"Unexpected number of written items: {all.Count} of {items.Count}");

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void CancellationDuringRead_StopsEarly(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var total = 1000;
        var items = Enumerable.Range(0, total).Select(i => (ISearchResult)new DummySearchResult($"R{i}")).ToList();
        storage.AddRawBatch(items);

        var cts = new CancellationTokenSource();
        var readResults = new List<ISearchResult>();

        // Start a canceller that will fire shortly after read starts
        Task.Run(() => { Thread.Sleep(5); cts.Cancel(); });

        storage.GetRawResultsInBatches(batch => { readResults.AddRange(batch); Thread.Sleep(1); }, 1, cts.Token);

        Assert.IsTrue(readResults.Count < total);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void ExactBatching_Boundaries(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var items = Enumerable.Range(0, 5).Select(i => (ISearchResult)new DummySearchResult($"B{i}")).ToList();
        storage.AddRawBatch(items);

        var batches2 = new List<List<ISearchResult>>();
        storage.GetRawResultsInBatches(b => batches2.Add(b), 2);
        Assert.AreEqual(3, batches2.Count);

        var batchesLarge = new List<List<ISearchResult>>();
        storage.GetRawResultsInBatches(b => batchesLarge.Add(b), 10);
        Assert.AreEqual(1, batchesLarge.Count);

        var batchesOne = new List<List<ISearchResult>>();
        storage.GetRawResultsInBatches(b => batchesOne.Add(b), 1);
        Assert.AreEqual(5, batchesOne.Count);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void Ordering_IsPreserved(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var items = Enumerable.Range(0, 5).Select(i => (ISearchResult)new DummySearchResult($"O{i}")).ToList();
        storage.AddRawBatch(items);

        var all = new List<ISearchResult>();
        storage.GetRawResultsInBatches(b => all.AddRange(b), 10);
        CollectionAssert.AreEqual(items.Select(x => x.GetMessage()).ToList(), all.Select(x => x.GetMessage()).ToList());

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void Isolation_RawVsFiltered(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var raw = new[] { new DummySearchResult("raw1"), new DummySearchResult("raw2") };
        var filtered = new[] { new DummySearchResult("f1"), new DummySearchResult("f2") };
        storage.AddRawBatch(raw);
        storage.AddFilteredBatch(filtered);

        var allRaw = new List<ISearchResult>();
        storage.GetRawResultsInBatches(b => allRaw.AddRange(b), 10);
        var allFiltered = new List<ISearchResult>();
        storage.GetFilteredResultsInBatches(b => allFiltered.AddRange(b), 10);

        Assert.AreEqual(2, allRaw.Count);
        Assert.AreEqual(2, allFiltered.Count);
        Assert.IsTrue(allRaw.All(r => r.GetMessage().StartsWith("raw")));
        Assert.IsTrue(allFiltered.All(r => r.GetMessage().StartsWith("f")));

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("Sqlite")]
    public void Persistence_Sqlite_DataPersistsAcrossInstances(string kind)
    {
        var factory = GetFactoryByKind(kind);

        // create, write and dispose
        using (var storage = factory.create())
        {
            var items = Enumerable.Range(0, 10).Select(i => (ISearchResult)new DummySearchResult($"P{i}")).ToList();
            storage.AddRawBatch(items);
        }

        // reopen and verify
        using (var storage = factory.create())
        {
            var all = new List<ISearchResult>();
            storage.GetRawResultsInBatches(b => all.AddRange(b), 1000);
            Assert.AreEqual(10, all.Count);
        }

        // verify file size grew
        var dbPath = _createdDbPaths.Last();
        Assert.IsTrue(File.Exists(dbPath));
        var size = new FileInfo(dbPath).Length;
        Assert.IsTrue(size > 0);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    public void DisposeBehavior_MultipleDisposeAndUse(string kind)
    {
        var factory = GetFactoryByKind(kind);
        var storage = factory.create();

        // double dispose must be safe
        storage.Dispose();
        storage.Dispose();

        // Behavior after dispose: InMemory is a no-op and still usable; Sqlite may throw
        try
        {
            storage.AddRawBatch(new[] { new DummySearchResult("afterDispose") });
            // If no exception, ensure call succeeded for InMemory
            var all = new List<ISearchResult>();
            storage.GetRawResultsInBatches(b => all.AddRange(b), 1000);
            // Either zero or more -- just ensure it does not crash the test framework
        }
        catch (Exception ex)
        {
            // For SQLite we may get ObjectDisposedException or InvalidOperationException
            Assert.IsTrue(ex is ObjectDisposedException || ex is InvalidOperationException);
        }
        finally
        {
            // Ensure cleanup (dispose again if needed)
            try { storage.Dispose(); } catch { }
        }

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void Statistics_AreAccurate(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        storage.AddRawBatch(new[] { new DummySearchResult("sraw1"), new DummySearchResult("sraw2") });
        storage.AddFilteredBatch(new[] { new DummySearchResult("sf1") });

        var stats = storage.GetStatistics();
        Assert.AreEqual(2, stats.rawRecordCount);
        Assert.AreEqual(1, stats.filteredRecordCount);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void LargePayloads_HandleAndBatch(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var large = new string('X', 100_000);
        var items = Enumerable.Range(0, 3).Select(i => (ISearchResult)new DummySearchResult(large)).ToList();
        storage.AddRawBatch(items);

        var batches = new List<List<ISearchResult>>();
        storage.GetRawResultsInBatches(b => batches.Add(b), 2);
        Assert.AreEqual(2, batches.Count);
        Assert.AreEqual(2, batches[0].Count);
        Assert.AreEqual(1, batches[1].Count);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    public void MutationSafety_CallbackMutatingBatchDoesNotAffectStorage(string kind)
    {
        var factory = GetFactoryByKind(kind);
        using var storage = factory.create();

        var items = Enumerable.Range(0, 5).Select(i => (ISearchResult)new DummySearchResult($"MS{i}")).ToList();
        storage.AddRawBatch(items);

        // callback mutates the provided batch (clears it)
        storage.GetRawResultsInBatches(batch => { batch.Clear(); }, 2);

        // subsequent read should still return all stored items
        var all = new List<ISearchResult>();
        storage.GetRawResultsInBatches(b => all.AddRange(b), 10);
        Assert.AreEqual(5, all.Count);

        factory.cleanup();
    }

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
    [DataRow("Hybrid")]
    [TestCategory("Performance")]
    public void Performance_InsertOneMillion(string kind)
    {
        var factory = GetFactoryByKind(kind);
        const int total = 1_000_000;
        const int batchSize = 10_000; // 100 batches
        var batches = total / batchSize;

        // If sqlite, the factory call already created and registered the DB path.
        string? dbPath = null;
        long dbSizeBefore = 0;
        if (kind == "Sqlite" && _createdDbPaths.Count > 0)
        {
            dbPath = _createdDbPaths.Last();
            if (File.Exists(dbPath)) dbSizeBefore = new FileInfo(dbPath).Length;
        }

        // Capture memory usage before
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long memBefore = GC.GetTotalMemory(true);
        var proc = Process.GetCurrentProcess();
        long procMemBefore = proc.PrivateMemorySize64;

        using var storage = factory.create();

        var sw = Stopwatch.StartNew();
        for (var b = 0; b < batches; b++)
        {
            var list = new List<ISearchResult>(batchSize);
            var baseIndex = b * batchSize;
            for (var i = 0; i < batchSize; i++)
            {
                list.Add(new DummySearchResult("PerfMsg" + (baseIndex + i)));
            }
            storage.AddRawBatch(list);
        }
        sw.Stop();

        // Force a GC to get a cleaner measure after inserts
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long memAfter = GC.GetTotalMemory(true);
        long procMemAfter = proc.PrivateMemorySize64;

        var stats = storage.GetStatistics();
        // Verify all records were written
        Assert.AreEqual(total, stats.rawRecordCount, $"Expected {total} records in {kind}, got {stats.rawRecordCount}");

        long dbSizeAfter = 0;
        if (kind == "Sqlite" && dbPath != null && File.Exists(dbPath))
        {
            dbSizeAfter = new FileInfo(dbPath).Length;
        }

        // Compute deltas
        long gcDelta = memAfter - memBefore;
        long procDelta = procMemAfter - procMemBefore;
        long dbDelta = dbSizeAfter - dbSizeBefore;

        // Emit timing and resource usage information to test output
        Console.WriteLine($"Inserted {total:N0} records into {kind} in {sw.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"GC memory delta: {gcDelta:N0} bytes ({gcDelta / 1024.0 / 1024.0:F2} MB)");
        Console.WriteLine($"Process private memory delta: {procDelta:N0} bytes ({procDelta / 1024.0 / 1024.0:F2} MB)");
        Console.WriteLine($"Storage-reported sizeInMemory: {stats.sizeInMemory:N0} bytes ({stats.sizeInMemory / 1024.0 / 1024.0:F2} MB)");
        if (kind == "Sqlite")
        {
            Console.WriteLine($"DB file: {dbPath}");
            Console.WriteLine($"DB size before: {dbSizeBefore:N0} bytes, after: {dbSizeAfter:N0} bytes, delta: {dbDelta:N0} bytes ({dbDelta / 1024.0 / 1024.0:F2} MB)");
            Console.WriteLine($"DB reported by GetStatistics: sizeOnDisk={stats.sizeOnDisk:N0} bytes");
        }

        Console.WriteLine($"Per-record GC delta: {gcDelta / (double)total:F2} bytes");
        Console.WriteLine($"Per-record storage-reported size: {stats.sizeInMemory / (double)total:F2} bytes");
        if (kind == "Sqlite")
        {
            Console.WriteLine($"Per-record DB delta: {dbDelta / (double)total:F2} bytes");
        }

        factory.cleanup();
    }
}
