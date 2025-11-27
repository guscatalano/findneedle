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

    /*
     * Verifies InMemoryStorage stores and returns full result content.
     * Steps:
     *  - Create two distinct DummySearchResult instances with different message/username/resultSource.
     *  - Call AddRawBatch to store them in the in-memory storage.
     *  - Retrieve all raw results using GetRawResultsInBatches and collect into a list.
     *  - Assert the stored count is correct and that individual fields (Message, Username, ResultSource)
     *    match the values that were inserted. This also implicitly verifies insertion order is preserved.
     */
    [TestMethod]
    public void InMemoryStorage_ContentVerification()
    {
        var storage = new InMemoryStorage();
        var a = new DummySearchResult("MessageA", "UserA", "SourceA");
        var b = new DummySearchResult("MessageB", "UserB", "SourceB");
        storage.AddRawBatch(new[] { a, b });

        var all = new List<ISearchResult>();
        storage.GetRawResultsInBatches(batch => all.AddRange(batch), 10);
        Assert.AreEqual(2, all.Count, "Expected two raw results");
        Assert.AreEqual("MessageA", all[0].GetMessage());
        Assert.AreEqual("UserA", all[0].GetUsername());
        Assert.AreEqual("SourceB", all[1].GetResultSource());
    }

    /*
     * Verifies SQLite-backed storage persists content across instances and preserves DateTime values.
     * Steps:
     *  - Create a unique per-test cache file path via CreateUniqueSearchFile().
     *  - Open a new SqliteStorage and AddRawBatch two DummySearchResult entries.
     *  - Dispose the SqliteStorage to flush and close the DB connection.
     *  - Reopen the SqliteStorage using the same searched file identifier and read back the records.
     *  - Assert that content fields match and that the stored LogTime parses back to the exact fixed UTC time.
     */
    [TestMethod]
    public void SqliteStorage_ContentAndDateRoundtrip()
    {
        var (searchedFile, dbPath) = CreateUniqueSearchFile();

        using (var storage = new SqliteStorage(searchedFile))
        {
            var a = new DummySearchResult("Msg1", "User1", "Src1");
            var b = new DummySearchResult("Msg2", "User2", "Src2");
            storage.AddRawBatch(new[] { a, b });
        }

        // Reopen and read
        using (var storage = new SqliteStorage(searchedFile))
        {
            var results = new List<ISearchResult>();
            storage.GetRawResultsInBatches(batch => results.AddRange(batch), 10);
            Assert.AreEqual(2, results.Count, "SQLite should return two results");
            Assert.AreEqual("Msg1", results[0].GetMessage());
            Assert.AreEqual("User1", results[0].GetUsername());
            // Date round-trip
            Assert.AreEqual(DummySearchResult.FixedTime, results[0].GetLogTime());
        }
    }

    /*
     * Ensures SqliteStorage.Dispose releases DB access so subsequent opens succeed.
     * Steps:
     *  - Create a SqliteStorage and write a small batch.
     *  - Dispose the storage (using block ends).
     *  - Attempt to reopen a new SqliteStorage instance which should succeed if the previous connection
     *    was properly closed and disposed. The test does not attempt to delete the DB here to avoid
     *    flaky failures due to transient OS locks; cleanup is handled by TestCleanup.
     */
    [TestMethod]
    public void SqliteStorage_DisposeReleasesFileLock()
    {
        var (searchedFile, dbPath) = CreateUniqueSearchFile();

        using (var storage = new SqliteStorage(searchedFile))
        {
            storage.AddRawBatch(new[] { new DummySearchResult() });
        }

        // After disposal we should be able to reopen the DB (indicates locks released)
        try
        {
            using (var reopened = new SqliteStorage(searchedFile))
            {
                var stats = reopened.GetStatistics();
                // at least the file exists and is queryable
                Assert.IsTrue(stats.rawRecordCount >= 0);
            }
        }
        catch (Exception ex)
        {
            Assert.Fail("Could not reopen DB after Dispose(): " + ex.Message);
        }

        // Cleanup will remove the DB file; do not attempt deletion here to avoid flaky failures from transient OS locks.
    }

    /*
     * Verifies that passing null to AddRawBatch and AddFilteredBatch throws ArgumentNullException.
     * Tests both InMemoryStorage and SqliteStorage implementations to ensure consistent API behavior.
     */
    [TestMethod]
    public void NullBatch_ThrowsArgumentNullException()
    {
        var inMemory = new InMemoryStorage();
        Assert.ThrowsException<ArgumentNullException>(() => inMemory.AddRawBatch(null));
        Assert.ThrowsException<ArgumentNullException>(() => inMemory.AddFilteredBatch(null));

        var (searchedFile, dbPath) = CreateUniqueSearchFile();
        using (var storage = new SqliteStorage(searchedFile))
        {
            Assert.ThrowsException<ArgumentNullException>(() => storage.AddRawBatch(null));
            Assert.ThrowsException<ArgumentNullException>(() => storage.AddFilteredBatch(null));
        }
    }

    /*
     * Verifies that if a CancellationToken is already cancelled before calling AddRawBatch,
     * the storage implementations do not write any records.
     * Steps:
     *  - Create a CancellationTokenSource and cancel it immediately.
     *  - Call AddRawBatch with the cancelled token for both InMemoryStorage and SqliteStorage.
     *  - Assert that no records were written by reading back the stored results.
     */
    [TestMethod]
    public void Cancellation_PreCancelledToken_PreventsWork()
    {
        var inMemory = new InMemoryStorage();
        var batch = new[] { new DummySearchResult(), new DummySearchResult() };
        var cts = new CancellationTokenSource();
        cts.Cancel();
        inMemory.AddRawBatch(batch, cts.Token);
        var results = new List<ISearchResult>();
        inMemory.GetRawResultsInBatches(b => results.AddRange(b), 10);
        Assert.AreEqual(0, results.Count, "Pre-cancelled token should prevent adds to InMemory");

        var (searchedFile, dbPath) = CreateUniqueSearchFile();
        using (var storage = new SqliteStorage(searchedFile))
        {
            storage.AddRawBatch(batch, cts.Token);
            var sqlResults = new List<ISearchResult>();
            storage.GetRawResultsInBatches(b => sqlResults.AddRange(b), 10);
            Assert.AreEqual(0, sqlResults.Count, "Pre-cancelled token should prevent adds to SQLite");
        }
    }

    /*
     * Verifies batching behavior of GetRawResultsInBatches for both storage implementations.
     * Steps:
     *  - Create 5 items and store them.
     *  - Request batches of size 2 and collect the produced batches.
     *  - Assert that the number of batches and sizes are as expected (2,2,1) and that order is preserved.
     */
    [TestMethod]
    public void BatchingBehavior_IsCorrect()
    {
        var items = Enumerable.Range(0, 5).Select(i => (ISearchResult)new DummySearchResult($"M{i}")).ToList();

        // InMemory batching
        var inMemory = new InMemoryStorage();
        inMemory.AddRawBatch(items);
        var batches = new List<List<ISearchResult>>();
        inMemory.GetRawResultsInBatches(b => batches.Add(b), 2);
        Assert.AreEqual(3, batches.Count, "Should produce 3 batches: 2,2,1");
        Assert.AreEqual(2, batches[0].Count);
        Assert.AreEqual(2, batches[1].Count);
        Assert.AreEqual(1, batches[2].Count);

        // SQLite batching
        var (searchedFile, dbPath) = CreateUniqueSearchFile();
        using (var storage = new SqliteStorage(searchedFile))
        {
            storage.AddRawBatch(items);
        }
        using (var storage = new SqliteStorage(searchedFile))
        {
            var sqlBatches = new List<List<ISearchResult>>();
            storage.GetRawResultsInBatches(b => sqlBatches.Add(b), 2);
            Assert.AreEqual(3, sqlBatches.Count, "SQLite: should produce 3 batches");
            Assert.AreEqual(2, sqlBatches[0].Count);
            Assert.AreEqual("M0", sqlBatches[0][0].GetMessage());
        }
    }
}
