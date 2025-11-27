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

    private (Func<ISearchStorage> create, Action cleanup) GetFactoryByKind(string kind)
    {
        return kind switch
        {
            "InMemory" => InMemoryFactory(),
            "Sqlite" => SqliteFactory(),
            _ => throw new ArgumentException("Unknown storage kind: " + kind, nameof(kind)),
        };
    }

    // Parameterized tests using DataTestMethod to run each scenario for both implementations.

    [DataTestMethod]
    [DataRow("InMemory")]
    [DataRow("Sqlite")]
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
}
