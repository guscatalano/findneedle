using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using FindPluginCore.Implementations.Storage;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests
{
    [TestClass]
    public class InMemoryAndSqliteStorageTests
    {
        private class DummySearchResult : ISearchResult
        {
            public DateTime GetLogTime() => DateTime.Now;
            public string GetMachineName() => "TestMachine";
            public void WriteToConsole() { }
            public Level GetLevel() => Level.Error;
            public string GetUsername() => "TestUser";
            public string GetTaskName() => "TestTask";
            public string GetOpCode() => "TestOp";
            public string GetSource() => "TestSource";
            public string GetSearchableData() => "TestData";
            public string GetMessage() => "TestMessage";
            public string GetResultSource() => "TestResultSource";
        }

        [TestMethod]
        public void InMemoryStorage_BasicBatchAndRetrieve()
        {
            var storage = new InMemoryStorage();
            var batch = new List<ISearchResult>
            {
                new DummySearchResult(),
                new DummySearchResult()
            };
            storage.AddRawBatch(batch);
            storage.AddFilteredBatch(batch);

            var rawResults = new List<ISearchResult>();
            storage.GetRawResultsInBatches(b => rawResults.AddRange(b), 1);
            Assert.AreEqual(2, rawResults.Count);

            var filteredResults = new List<ISearchResult>();
            storage.GetFilteredResultsInBatches(b => filteredResults.AddRange(b), 1);
            Assert.AreEqual(2, filteredResults.Count);

            var stats = storage.GetStatistics();
            Assert.AreEqual(2, stats.rawRecordCount);
            Assert.AreEqual(2, stats.filteredRecordCount);
        }

        [TestMethod]
        public void SqliteStorage_BasicBatchAndRetrieve()
        {
            string tempFile = Path.GetTempFileName();
            try
            {
                var storage = new SqliteStorage(tempFile);
                var batch = new List<ISearchResult>
                {
                    new DummySearchResult(),
                    new DummySearchResult()
                };
                storage.AddRawBatch(batch);
                storage.AddFilteredBatch(batch);

                var rawResults = new List<ISearchResult>();
                storage.GetRawResultsInBatches(b => rawResults.AddRange(b), 1);
                Assert.AreEqual(2, rawResults.Count);

                var filteredResults = new List<ISearchResult>();
                storage.GetFilteredResultsInBatches(b => filteredResults.AddRange(b), 1);
                Assert.AreEqual(2, filteredResults.Count);

                var stats = storage.GetStatistics();
                Assert.AreEqual(2, stats.rawRecordCount);
                Assert.AreEqual(2, stats.filteredRecordCount);
                Assert.IsTrue(stats.sizeOnDisk > 0);
            }
            finally
            {
                if (File.Exists(tempFile + ".db"))
                    File.Delete(tempFile + ".db");
            }
        }
    }
}
