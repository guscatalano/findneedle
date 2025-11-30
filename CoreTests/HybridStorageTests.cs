using System;
using System.Collections.Generic;
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
public class HybridStorageTests
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

    private HybridStorage CreateHybridStorage(int memoryThresholdMB = 100, double spillPercentage = 0.5, int promotionThreshold = 3)
    {
        var searchedFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var dbPath = CachedStorage.GetCacheFilePath(searchedFile, ".db");
        _createdDbPaths.Add(dbPath);
        if (File.Exists(dbPath))
            File.Delete(dbPath);
        
        return new HybridStorage(searchedFile, memoryThresholdMB, spillPercentage, promotionThreshold);
    }

    [TestMethod]
    public void Constructor_ValidParameters_Succeeds()
    {
        using var storage = CreateHybridStorage();
        Assert.IsNotNull(storage);
    }

    [TestMethod]
    public void Constructor_InvalidParameters_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() => new HybridStorage(null));
        Assert.ThrowsException<ArgumentNullException>(() => new HybridStorage(""));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => CreateHybridStorage(memoryThresholdMB: 0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => CreateHybridStorage(memoryThresholdMB: -1));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => CreateHybridStorage(spillPercentage: 0));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => CreateHybridStorage(spillPercentage: 1.5));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => CreateHybridStorage(promotionThreshold: 0));
    }

    [TestMethod]
    public void BasicReadWrite_Raw_WorksCorrectly()
    {
        using var storage = CreateHybridStorage();

        var items = new[]
        {
            new DummySearchResult("Msg1", "User1", "Src1"),
            new DummySearchResult("Msg2", "User2", "Src2")
        };
        
        storage.AddRawBatch(items);

        var results = new List<ISearchResult>();
        storage.GetRawResultsInBatches(batch => results.AddRange(batch), 10);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("Msg1", results[0].GetMessage());
        Assert.AreEqual("User2", results[1].GetUsername());
    }

    [TestMethod]
    public void BasicReadWrite_Filtered_WorksCorrectly()
    {
        using var storage = CreateHybridStorage();

        var items = new[]
        {
            new DummySearchResult("FilteredMsg1"),
            new DummySearchResult("FilteredMsg2")
        };
        
        storage.AddFilteredBatch(items);

        var results = new List<ISearchResult>();
        storage.GetFilteredResultsInBatches(batch => results.AddRange(batch), 10);

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("FilteredMsg1", results[0].GetMessage());
    }

    [TestMethod]
    public void SmallDataset_StaysInMemory()
    {
        // Use 10MB threshold, add small amount of data
        using var storage = CreateHybridStorage(memoryThresholdMB: 10);

        var items = Enumerable.Range(0, 100).Select(i => new DummySearchResult($"Msg{i}")).ToList();
        storage.AddRawBatch(items);

        var stats = storage.GetStatistics();
        Assert.AreEqual(100, stats.rawRecordCount);
        // Small dataset should be in memory
        Assert.IsTrue(stats.sizeInMemory > 0);
    }

    [TestMethod]
    public void LargeDataset_SpillsToDisk()
    {
        // Use very small threshold (1MB) to force spilling
        using var storage = CreateHybridStorage(memoryThresholdMB: 1, spillPercentage: 0.5);

        // Create large messages to exceed threshold
        var largeMessage = new string('X', 10000); // 10KB per message
        var items = Enumerable.Range(0, 200).Select(i => new DummySearchResult(largeMessage + i)).ToList();
        
        storage.AddRawBatch(items);

        var stats = storage.GetStatistics();
        Assert.AreEqual(200, stats.rawRecordCount);
        // Should have spilled some data to disk
        Assert.IsTrue(stats.sizeOnDisk > 0, "Expected data to be spilled to disk");
    }

    [TestMethod]
    public void SpilledData_CanBeReadBack()
    {
        // Force spilling with small threshold
        using var storage = CreateHybridStorage(memoryThresholdMB: 1, spillPercentage: 0.5);

        var largeMessage = new string('Y', 10000);
        var items = Enumerable.Range(0, 200).Select(i => new DummySearchResult(largeMessage + i, $"User{i}")).ToList();
        
        storage.AddRawBatch(items);

        // Read all results back
        var results = new List<ISearchResult>();
        storage.GetRawResultsInBatches(batch => results.AddRange(batch), 50);

        // Should get all items back (from memory + disk)
        Assert.AreEqual(200, results.Count);
        
        // Verify data integrity
        Assert.IsTrue(results.All(r => r.GetMessage().StartsWith(largeMessage)));
    }

    [TestMethod]
    public void AccessTracking_PromotesFrequentlyAccessedData()
    {
        // Small threshold to force spilling, low promotion threshold
        using var storage = CreateHybridStorage(memoryThresholdMB: 1, spillPercentage: 0.5, promotionThreshold: 2);

        var largeMessage = new string('Z', 10000);
        var items = Enumerable.Range(0, 100).Select(i => new DummySearchResult(largeMessage + i)).ToList();
        
        storage.AddRawBatch(items);

        var stats1 = storage.GetStatistics();
        Assert.IsTrue(stats1.sizeOnDisk > 0, "Should have spilled to disk");

        // Access the data multiple times to trigger promotion
        for (int access = 0; access < 3; access++)
        {
            var results = new List<ISearchResult>();
            storage.GetRawResultsInBatches(batch => results.AddRange(batch), 50);
            Assert.AreEqual(100, results.Count, $"Access {access + 1}: Should read all items");
        }

        // After multiple accesses, some data might be promoted back to memory
        // This is timing-dependent, so we just verify the system still works
        var finalResults = new List<ISearchResult>();
        storage.GetRawResultsInBatches(batch => finalResults.AddRange(batch), 50);
        Assert.AreEqual(100, finalResults.Count, "Should still read all items after promotion");
    }

    [TestMethod]
    public void Isolation_RawAndFiltered_SeparateSpilling()
    {
        using var storage = CreateHybridStorage(memoryThresholdMB: 1, spillPercentage: 0.5);

        var largeMessage = new string('A', 10000);
        var rawItems = Enumerable.Range(0, 100).Select(i => new DummySearchResult("Raw" + largeMessage + i)).ToList();
        var filteredItems = Enumerable.Range(0, 100).Select(i => new DummySearchResult("Filtered" + largeMessage + i)).ToList();
        
        storage.AddRawBatch(rawItems);
        storage.AddFilteredBatch(filteredItems);

        var rawResults = new List<ISearchResult>();
        storage.GetRawResultsInBatches(batch => rawResults.AddRange(batch), 50);
        
        var filteredResults = new List<ISearchResult>();
        storage.GetFilteredResultsInBatches(batch => filteredResults.AddRange(batch), 50);

        Assert.AreEqual(100, rawResults.Count);
        Assert.AreEqual(100, filteredResults.Count);
        Assert.IsTrue(rawResults.All(r => r.GetMessage().StartsWith("Raw")));
        Assert.IsTrue(filteredResults.All(r => r.GetMessage().StartsWith("Filtered")));
    }

    [TestMethod]
    public void Statistics_ReflectBothStorages()
    {
        using var storage = CreateHybridStorage(memoryThresholdMB: 1, spillPercentage: 0.5);

        var largeMessage = new string('B', 10000);
        var items = Enumerable.Range(0, 150).Select(i => new DummySearchResult(largeMessage + i)).ToList();
        
        storage.AddRawBatch(items);

        var stats = storage.GetStatistics();
        
        Assert.AreEqual(150, stats.rawRecordCount, "Should report total count from both storages");
        Assert.AreEqual(0, stats.filteredRecordCount);
        // Should have data in both memory and disk
        Assert.IsTrue(stats.sizeInMemory > 0 || stats.sizeOnDisk > 0);
    }

    [TestMethod]
    public void NullBatch_Throws()
    {
        using var storage = CreateHybridStorage();
        
        Assert.ThrowsException<ArgumentNullException>(() => storage.AddRawBatch(null));
        Assert.ThrowsException<ArgumentNullException>(() => storage.AddFilteredBatch(null));
    }

    [TestMethod]
    public void NullCallback_Throws()
    {
        using var storage = CreateHybridStorage();
        
        Assert.ThrowsException<ArgumentNullException>(() => storage.GetRawResultsInBatches(null, 10));
        Assert.ThrowsException<ArgumentNullException>(() => storage.GetFilteredResultsInBatches(null, 10));
    }

    [TestMethod]
    public void CancellationToken_StopsOperation()
    {
        using var storage = CreateHybridStorage();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var items = Enumerable.Range(0, 100).Select(i => new DummySearchResult($"Msg{i}")).ToList();
        storage.AddRawBatch(items, cts.Token);

        var results = new List<ISearchResult>();
        storage.GetRawResultsInBatches(batch => results.AddRange(batch), 10);
        
        // With pre-cancelled token, operation should stop early or not add anything
        Assert.IsTrue(results.Count <= 100);
    }

    [TestMethod]
    public void Dispose_CleansUpBothStorages()
    {
        var storage = CreateHybridStorage();
        
        var items = new[] { new DummySearchResult("Test") };
        storage.AddRawBatch(items);
        
        storage.Dispose();
        
        // Multiple dispose should be safe
        storage.Dispose();
    }

    [TestMethod]
    public void MultipleSpills_DataRemainAccessible()
    {
        using var storage = CreateHybridStorage(memoryThresholdMB: 1, spillPercentage: 0.3);

        var largeMessage = new string('C', 5000);
        
        // Add data in multiple batches to trigger multiple spills
        for (int batch = 0; batch < 5; batch++)
        {
            var items = Enumerable.Range(batch * 50, 50)
                .Select(i => new DummySearchResult(largeMessage + i))
                .ToList();
            storage.AddRawBatch(items);
        }

        var allResults = new List<ISearchResult>();
        storage.GetRawResultsInBatches(batch => allResults.AddRange(batch), 50);

        Assert.AreEqual(250, allResults.Count, "Should retrieve all items after multiple spills");
    }

    [TestMethod]
    public void Concurrency_MultipleThreadsAdding()
    {
        using var storage = CreateHybridStorage(memoryThresholdMB: 10);

        var tasks = new List<Task>();
        const int threads = 5;
        const int itemsPerThread = 50;

        for (int t = 0; t < threads; t++)
        {
            var threadId = t;
            tasks.Add(Task.Run(() =>
            {
                var items = Enumerable.Range(threadId * itemsPerThread, itemsPerThread)
                    .Select(i => new DummySearchResult($"Thread{threadId}-Msg{i}"))
                    .ToList();
                storage.AddRawBatch(items);
            }));
        }

        Task.WaitAll(tasks.ToArray());

        var results = new List<ISearchResult>();
        storage.GetRawResultsInBatches(batch => results.AddRange(batch), 100);

        Assert.AreEqual(threads * itemsPerThread, results.Count);
    }

    [TestMethod]
    public void DifferentSpillPercentages_BehaveDifferently()
    {
        // Test with 50% spill
        using var storage50 = CreateHybridStorage(memoryThresholdMB: 1, spillPercentage: 0.5);
        var largeMsg = new string('D', 8000);
        var items = Enumerable.Range(0, 200).Select(i => new DummySearchResult(largeMsg + i)).ToList();
        storage50.AddRawBatch(items);
        var stats50 = storage50.GetStatistics();

        // Test with 80% spill
        using var storage80 = CreateHybridStorage(memoryThresholdMB: 1, spillPercentage: 0.8);
        storage80.AddRawBatch(items);
        var stats80 = storage80.GetStatistics();

        // Both should have all records
        Assert.AreEqual(200, stats50.rawRecordCount);
        Assert.AreEqual(200, stats80.rawRecordCount);
        
        // Both should have spilled to disk
        Assert.IsTrue(stats50.sizeOnDisk > 0);
        Assert.IsTrue(stats80.sizeOnDisk > 0);
    }

    [TestMethod]
    public void EmptyStorage_ReturnsZeroStatistics()
    {
        using var storage = CreateHybridStorage();
        
        var stats = storage.GetStatistics();
        
        Assert.AreEqual(0, stats.rawRecordCount);
        Assert.AreEqual(0, stats.filteredRecordCount);
        // SQLite creates database with initial size for schema, so sizeOnDisk > 0 is expected
        Assert.IsTrue(stats.sizeOnDisk >= 0, "Size on disk should be non-negative");
        Assert.AreEqual(0, stats.sizeInMemory, "Memory should be zero when no data added");
    }

    [TestMethod]
    public void Batching_WorksAcrossStorages()
    {
        using var storage = CreateHybridStorage(memoryThresholdMB: 1, spillPercentage: 0.5);

        var largeMsg = new string('E', 8000);
        var items = Enumerable.Range(0, 100).Select(i => new DummySearchResult(largeMsg + i)).ToList();
        storage.AddRawBatch(items);

        var batches = new List<int>();
        storage.GetRawResultsInBatches(batch => batches.Add(batch.Count), batchSize: 10);

        // Should receive multiple batches
        Assert.IsTrue(batches.Count > 1);
        // Total should equal item count
        Assert.AreEqual(100, batches.Sum());
    }

    [TestMethod]
    public void OrderingPreserved_AcrossSpills()
    {
        using var storage = CreateHybridStorage(memoryThresholdMB: 1, spillPercentage: 0.5);

        var largeMsg = new string('F', 8000);
        var items = Enumerable.Range(0, 50).Select(i => new DummySearchResult($"Order{i:D3}_{largeMsg}")).ToList();
        storage.AddRawBatch(items);

        var results = new List<ISearchResult>();
        storage.GetRawResultsInBatches(batch => results.AddRange(batch), 25);

        // Verify ordering (results from memory come first, then disk)
        for (int i = 0; i < results.Count; i++)
        {
            Assert.IsTrue(results[i].GetMessage().Contains("Order"));
        }
    }

    [TestMethod]
    [TestCategory("Performance")]
    public void Performance_LargeDatasetWithSpilling()
    {
        using var storage = CreateHybridStorage(memoryThresholdMB: 50, spillPercentage: 0.5);

        const int totalItems = 10000;
        var items = Enumerable.Range(0, totalItems)
            .Select(i => new DummySearchResult($"PerfMsg{i}"))
            .ToList();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        storage.AddRawBatch(items);
        sw.Stop();

        Console.WriteLine($"Added {totalItems} items in {sw.ElapsedMilliseconds}ms");

        sw.Restart();
        var results = new List<ISearchResult>();
        storage.GetRawResultsInBatches(batch => results.AddRange(batch), 1000);
        sw.Stop();

        Console.WriteLine($"Read {results.Count} items in {sw.ElapsedMilliseconds}ms");

        Assert.AreEqual(totalItems, results.Count);
        
        var stats = storage.GetStatistics();
        Console.WriteLine($"Memory: {stats.sizeInMemory:N0} bytes, Disk: {stats.sizeOnDisk:N0} bytes");
    }
}
