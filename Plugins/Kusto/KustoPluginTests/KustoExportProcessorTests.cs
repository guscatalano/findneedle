using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FindNeedlePluginLib;
using KustoPlugin.FileExtension;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KustoPluginTests;

[TestClass]
public class KustoExportProcessorTests
{
    private KustoExportProcessor? _processor;
    private string? _testFilePath;

    [TestInitialize]
    public void Setup()
    {
        _processor = new KustoExportProcessor();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_testFilePath != null && File.Exists(_testFilePath))
        {
            try { File.Delete(_testFilePath); } catch { }
        }
    }

    private string CreateTestFile(string content)
    {
        _testFilePath = Path.Combine(Path.GetTempPath(), $"kusto-test-{Guid.NewGuid()}.txt");
        File.WriteAllText(_testFilePath, content);
        return _testFilePath;
    }

    private string GetSampleKustoFile()
    {
        // Try multiple paths to find the sample file
        var possiblePaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sample", "samplekusto.txt"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\Sample\\samplekusto.txt"),
            "Plugins\\Kusto\\KustoPluginTests\\Sample\\samplekusto.txt",
            "Sample\\samplekusto.txt"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
                return Path.GetFullPath(path);
        }

        // If sample file not found, create it from embedded content
        var tempPath = Path.Combine(Path.GetTempPath(), "samplekusto.txt");
        var sampleContent = @"PreciseTimeStamp	ActivityId	Pid	ProviderName	TaskName	Message	EventMessage	Level	HostInstance
2026-01-01 00:00:00.0000000	00000000-0000-0000-0000-000000000000	1000	Sample-Provider	SampleTask	msg=""Sample message"" SampleField=""sample-value"" GatewayUri=""https://sample-gateway-url""		4	SAMPLEHOST01.DOMAIN.LOCAL
2026-01-01 00:01:00.0000000	11111111-1111-1111-1111-111111111111	1001	Sample-Provider	SampleTask	msg=""Another sample message"" SampleField=""another-value"" GatewayUri=""https://another-sample-url""		4	SAMPLEHOST02.DOMAIN.LOCAL
2026-01-01 00:02:00.0000000	22222222-2222-2222-2222-222222222222	1002	Sample-Provider	SampleTask	msg=""Third sample message"" SampleField=""third-value"" GatewayUri=""https://third-sample-url""		4	SAMPLEHOST03.DOMAIN.LOCAL
";
        File.WriteAllText(tempPath, sampleContent);
        return tempPath;
    }

    [TestMethod]
    public void FileExtension_ReturnsTxt()
    {
        Assert.AreEqual(".txt", _processor?.FileExtension);
    }

    [TestMethod]
    public void RegisterForExtensions_ReturnsTxtExtension()
    {
        var extensions = _processor?.RegisterForExtensions();
        Assert.IsNotNull(extensions);
        Assert.AreEqual(1, extensions.Count);
        Assert.AreEqual(".txt", extensions[0]);
    }

    [TestMethod]
    public void OpenFile_ValidFile_Succeeds()
    {
        var testFile = CreateTestFile("PreciseTimeStamp\tProviderName\n2024-01-01 10:00:00\tTestProvider\n");
        _processor?.OpenFile(testFile);
        
        Assert.AreEqual(testFile, _processor?.GetFileName());
    }

    [TestMethod]
    public void OpenFile_ClearsResults()
    {
        var testFile = CreateTestFile("PreciseTimeStamp\tProviderName\n2024-01-01 10:00:00\tTestProvider\n");
        _processor?.OpenFile(testFile);
        _processor?.CheckFileFormat();
        _processor?.LoadInMemory();

        var resultsBeforeClear = _processor?.GetResults().Count;

        _processor?.OpenFile(testFile);
        var resultsAfterClear = _processor?.GetResults().Count;

        Assert.AreEqual(0, resultsAfterClear);
    }

    [TestMethod]
    public void CheckFileFormat_ValidKustoFormat_ReturnsTrue()
    {
        var testFile = CreateTestFile("PreciseTimeStamp\tProviderName\tMessage\n2024-01-01 10:00:00\tTestProvider\tTest message\n");
        _processor?.OpenFile(testFile);
        
        var isValid = _processor?.CheckFileFormat();
        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public void CheckFileFormat_MissingRequiredColumn_ReturnsFalse()
    {
        var testFile = CreateTestFile("Message\tLevel\n");
        _processor?.OpenFile(testFile);
        
        var isValid = _processor?.CheckFileFormat();
        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void CheckFileFormat_EmptyFile_ReturnsFalse()
    {
        var testFile = CreateTestFile("");
        _processor?.OpenFile(testFile);
        
        var isValid = _processor?.CheckFileFormat();
        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void CheckFileFormat_NonExistentFile_ReturnsFalse()
    {
        _processor?.OpenFile("/nonexistent/path/file.txt");
        
        var isValid = _processor?.CheckFileFormat();
        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void LoadInMemory_WithValidData_ParsesRecords()
    {
        var header = "PreciseTimeStamp\tProviderName\tMessage\tLevel\tHostInstance\n";
        var record1 = "2024-01-01 10:00:00\tTestProvider\tTest message 1\t4\tMachine1\n";
        var record2 = "2024-01-01 10:00:01\tTestProvider\tTest message 2\t3\tMachine2\n";

        var testFile = CreateTestFile(header + record1 + record2);
        _processor?.OpenFile(testFile);
        _processor?.CheckFileFormat();
        _processor?.LoadInMemory();

        var results = _processor?.GetResults();
        Assert.IsNotNull(results);
        Assert.AreEqual(2, results.Count);
    }

    [TestMethod]
    public void LoadInMemory_SkipsEmptyLines()
    {
        var header = "PreciseTimeStamp\tProviderName\tMessage\n";
        var record1 = "2024-01-01 10:00:00\tTestProvider\tMessage 1\n";
        var emptyLine = "\n";
        var record2 = "2024-01-01 10:00:01\tTestProvider\tMessage 2\n";

        var testFile = CreateTestFile(header + record1 + emptyLine + record2);
        _processor?.OpenFile(testFile);
        _processor?.CheckFileFormat();
        _processor?.LoadInMemory();

        var results = _processor?.GetResults();
        Assert.AreEqual(2, results.Count);
    }

    [TestMethod]
    public void LoadInMemory_MisalignedColumns_SkipsRecord()
    {
        var header = "PreciseTimeStamp\tProviderName\tMessage\n";
        var validRecord = "2024-01-01 10:00:00\tTestProvider\tMessage 1\n";
        var invalidRecord = "2024-01-01 10:00:01\tTestProvider\n"; // Missing column

        var testFile = CreateTestFile(header + validRecord + invalidRecord);
        _processor?.OpenFile(testFile);
        _processor?.CheckFileFormat();
        _processor?.LoadInMemory();

        var results = _processor?.GetResults();
        Assert.AreEqual(1, results.Count);
    }

    [TestMethod]
    public void LoadInMemory_CountsProviders()
    {
        var header = "PreciseTimeStamp\tProviderName\tMessage\n";
        var record1 = "2024-01-01 10:00:00\tProvider1\tMessage 1\n";
        var record2 = "2024-01-01 10:00:01\tProvider1\tMessage 2\n";
        var record3 = "2024-01-01 10:00:02\tProvider2\tMessage 3\n";

        var testFile = CreateTestFile(header + record1 + record2 + record3);
        _processor?.OpenFile(testFile);
        _processor?.CheckFileFormat();
        _processor?.LoadInMemory();

        var providerCount = _processor?.GetProviderCount();
        Assert.IsNotNull(providerCount);
        Assert.AreEqual(2, providerCount.Count);
        Assert.AreEqual(2, providerCount["Provider1"]);
        Assert.AreEqual(1, providerCount["Provider2"]);
    }

    [TestMethod]
    public void LoadInMemory_WithCancellation_RespectsCancelledToken()
    {
        var header = "PreciseTimeStamp\tProviderName\tMessage\n";
        var record = "2024-01-01 10:00:00\tTestProvider\tMessage 1\n";

        var testFile = CreateTestFile(header + record);
        _processor?.OpenFile(testFile);
        _processor?.CheckFileFormat();

        // Create an already-cancelled token
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _processor?.LoadInMemory(cts.Token);

        var results = _processor?.GetResults();
        Assert.IsNotNull(results);
        // Processing should be skipped due to pre-cancelled token
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void LoadInMemory_WithoutFormatCheck_DoesNothing()
    {
        var testFile = CreateTestFile("PreciseTimeStamp\tProviderName\n2024-01-01 10:00:00\tTestProvider\n");
        _processor?.OpenFile(testFile);
        // Don't call CheckFileFormat
        _processor?.LoadInMemory();

        var results = _processor?.GetResults();
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void DoPreProcessing_DoesNotThrow()
    {
        _processor?.DoPreProcessing();
        _processor?.DoPreProcessing(CancellationToken.None);
        // Should not throw
        Assert.IsTrue(true);
    }

    [TestMethod]
    public async Task GetResultsWithCallback_CallsCallbackPerBatch()
    {
        var header = "PreciseTimeStamp\tProviderName\tMessage\n";
        var records = string.Join("\n", Enumerable.Range(0, 2500)
            .Select(i => $"2024-01-01 10:00:00\tProvider\tMessage {i}"));

        var testFile = CreateTestFile(header + records);
        _processor?.OpenFile(testFile);
        _processor?.CheckFileFormat();
        _processor?.LoadInMemory();

        var batchCount = 0;
        var totalRecords = 0;

        await _processor!.GetResultsWithCallback(batch =>
        {
            batchCount++;
            totalRecords += batch.Count;
            Assert.IsTrue(batch.Count <= 1000);
        }, batchSize: 1000);

        Assert.AreEqual(2500, totalRecords);
        Assert.AreEqual(3, batchCount); // 1000 + 1000 + 500
    }

    [TestMethod]
    public void GetSearchPerformanceEstimate_ReturnsNull()
    {
        var estimate = _processor?.GetSearchPerformanceEstimate();
        Assert.IsNotNull(estimate);
        Assert.IsNull(estimate?.Item1);
        Assert.IsNull(estimate?.Item2);
    }

    [TestMethod]
    public void Dispose_DoesNotThrow()
    {
        _processor?.Dispose();
        // Should not throw
        Assert.IsTrue(true);
    }

    [TestMethod]
    public void GetResults_ReturnsISearchResults()
    {
        var header = "PreciseTimeStamp\tProviderName\tMessage\n";
        var record = "2024-01-01 10:00:00\tTestProvider\tTest message\n";

        var testFile = CreateTestFile(header + record);
        _processor?.OpenFile(testFile);
        _processor?.CheckFileFormat();
        _processor?.LoadInMemory();

        var results = _processor?.GetResults();
        Assert.IsNotNull(results);
        Assert.AreEqual(1, results.Count);
        Assert.IsInstanceOfType(results[0], typeof(ISearchResult));
    }

    [TestMethod]
    public void GetProviderCount_ReturnsNewDictionary()
    {
        var header = "PreciseTimeStamp\tProviderName\n";
        var record = "2024-01-01 10:00:00\tProvider1\n";

        var testFile = CreateTestFile(header + record);
        _processor?.OpenFile(testFile);
        _processor?.CheckFileFormat();
        _processor?.LoadInMemory();

        var count1 = _processor?.GetProviderCount();
        var count2 = _processor?.GetProviderCount();

        Assert.IsNotNull(count1);
        Assert.IsNotNull(count2);
        Assert.AreNotSame(count1, count2); // Different instances
        Assert.AreEqual(count1["Provider1"], count2["Provider1"]);
    }

    [TestMethod]
    public void GetFileName_WithoutOpeningFile_ReturnsEmpty()
    {
        var fileName = _processor?.GetFileName();
        Assert.AreEqual(string.Empty, fileName);
    }

    [TestMethod]
    public void LoadInMemory_WithRealWorldKustoExportData_ParsesCorrectly()
    {
        // Use actual sample Kusto export file
        var sampleFile = GetSampleKustoFile();
        
        _processor?.OpenFile(sampleFile);
        
        // Verify format check passes
        var isValidFormat = _processor?.CheckFileFormat();
        Assert.IsTrue(isValidFormat, "Kusto export format should be recognized");

        // Load the data
        _processor?.LoadInMemory();

        // Verify records were parsed
        var results = _processor?.GetResults();
        Assert.IsNotNull(results);
        Assert.AreEqual(3, results.Count, "Should have parsed 3 records from sample file");

        // Verify first record
        var firstResult = results[0] as KustoExportLogLine;
        Assert.IsNotNull(firstResult);
        Assert.AreEqual("00000000-0000-0000-0000-000000000000", firstResult.ActivityId);
        Assert.AreEqual("1000", firstResult.Pid);
        Assert.AreEqual("Sample-Provider", firstResult.ProviderName);
        Assert.AreEqual("SampleTask", firstResult.TaskName);
        Assert.AreEqual("SAMPLEHOST01.DOMAIN.LOCAL", firstResult.HostInstance);
        Assert.IsTrue(firstResult.Message.Contains("Sample message"));
        Assert.IsTrue(firstResult.Message.Contains("sample-value"));
        Assert.IsTrue(firstResult.Message.Contains("sample-gateway-url"));

        // Verify second record
        var secondResult = results[1] as KustoExportLogLine;
        Assert.IsNotNull(secondResult);
        Assert.AreEqual("11111111-1111-1111-1111-111111111111", secondResult.ActivityId);
        Assert.AreEqual("1001", secondResult.Pid);
        Assert.AreEqual("SAMPLEHOST02.DOMAIN.LOCAL", secondResult.HostInstance);

        // Verify third record
        var thirdResult = results[2] as KustoExportLogLine;
        Assert.IsNotNull(thirdResult);
        Assert.AreEqual("22222222-2222-2222-2222-222222222222", thirdResult.ActivityId);
        Assert.AreEqual("1002", thirdResult.Pid);
        Assert.AreEqual("SAMPLEHOST03.DOMAIN.LOCAL", thirdResult.HostInstance);
    }

    [TestMethod]
    public void LoadInMemory_WithRealWorldKustoExportData_ProviderCountingWorks()
    {
        var sampleFile = GetSampleKustoFile();
        _processor?.OpenFile(sampleFile);
        _processor?.CheckFileFormat();
        _processor?.LoadInMemory();

        var providerCount = _processor?.GetProviderCount();
        Assert.IsNotNull(providerCount);
        Assert.AreEqual(1, providerCount.Count, "Sample file has only one provider");
        Assert.AreEqual(3, providerCount["Sample-Provider"], "Should have 3 records from Sample-Provider");
    }

    [TestMethod]
    public void LoadInMemory_WithRealWorldKustoExportData_SearchabilityWorks()
    {
        var sampleFile = GetSampleKustoFile();
        _processor?.OpenFile(sampleFile);
        _processor?.CheckFileFormat();
        _processor?.LoadInMemory();

        var results = _processor?.GetResults();
        Assert.IsNotNull(results);
        Assert.AreEqual(3, results.Count);

        // Verify searchable data includes Message content
        var firstSearchable = (results[0] as KustoExportLogLine)?.GetSearchableData();
        Assert.IsTrue(firstSearchable?.Contains("Sample message") ?? false);
        Assert.IsTrue(firstSearchable?.Contains("gateway") ?? false);

        var secondSearchable = (results[1] as KustoExportLogLine)?.GetSearchableData();
        Assert.IsTrue(secondSearchable?.Contains("Another sample message") ?? false);

        var thirdSearchable = (results[2] as KustoExportLogLine)?.GetSearchableData();
        Assert.IsTrue(thirdSearchable?.Contains("Third sample message") ?? false);
    }

    [TestMethod]
    public void LoadInMemory_WithRealWorldKustoExportData_TimestampParsingWorks()
    {
        var sampleFile = GetSampleKustoFile();
        _processor?.OpenFile(sampleFile);
        _processor?.CheckFileFormat();
        _processor?.LoadInMemory();

        var results = _processor?.GetResults();
        Assert.IsNotNull(results);
        Assert.AreEqual(3, results.Count);

        var firstLog = results[0] as KustoExportLogLine;
        Assert.AreEqual(new DateTime(2026, 1, 1, 0, 0, 0), firstLog?.PreciseTimeStamp);

        var secondLog = results[1] as KustoExportLogLine;
        Assert.AreEqual(new DateTime(2026, 1, 1, 0, 1, 0), secondLog?.PreciseTimeStamp);

        var thirdLog = results[2] as KustoExportLogLine;
        Assert.AreEqual(new DateTime(2026, 1, 1, 0, 2, 0), thirdLog?.PreciseTimeStamp);
    }

    [TestMethod]
    public void LoadInMemory_WithRealWorldKustoExportData_LevelConversionWorks()
    {
        var sampleFile = GetSampleKustoFile();
        _processor?.OpenFile(sampleFile);
        _processor?.CheckFileFormat();
        _processor?.LoadInMemory();

        var results = _processor?.GetResults();
        Assert.IsNotNull(results);
        Assert.AreEqual(3, results.Count);

        // Sample file has all records with level "4" (Info)
        Assert.AreEqual(FindNeedlePluginLib.Level.Info, results[0].GetLevel());
        Assert.AreEqual(FindNeedlePluginLib.Level.Info, results[1].GetLevel());
        Assert.AreEqual(FindNeedlePluginLib.Level.Info, results[2].GetLevel());
    }

    [TestMethod]
    public async Task GetResultsWithCallback_WithRealWorldKustoExportData_BatchesCorrectly()
    {
        var sampleFile = GetSampleKustoFile();
        _processor?.OpenFile(sampleFile);
        _processor?.CheckFileFormat();
        _processor?.LoadInMemory();

        var batchCallCount = 0;
        var totalRecordsProcessed = 0;

        await _processor!.GetResultsWithCallback(batch =>
        {
            batchCallCount++;
            totalRecordsProcessed += batch.Count;
        }, batchSize: 1000);

        Assert.IsTrue(batchCallCount > 0, "Should have called callback");
        Assert.AreEqual(3, totalRecordsProcessed, "Should process all 3 records from sample file");
    }
}
