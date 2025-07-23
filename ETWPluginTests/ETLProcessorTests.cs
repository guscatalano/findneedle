global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Text;
global using System.Threading.Tasks;
using findneedle;
using findneedle.Implementations.FileExtensions;
using findneedle.WDK;
using FindNeedlePluginLib;


namespace ETWPluginTests;

[TestClass]
public sealed class ETWProcessorTests
{

    [TestInitialize]
    public void Init()
    {
        WDKFinder.ResetTestFlags();
    }

    [TestMethod]
    public void RegisterCorrectly()
    {
        ETLProcessor x = new ETLProcessor();
        var reg = x.RegisterForExtensions();
        Assert.IsTrue(reg.Count() == 3);
        Assert.IsTrue(reg.FirstOrDefault(x => x.Equals(".etl")) != null);
        x.Dispose();
    }

    [TestMethod]
    public void ParseSimple()
    {
        ETWTestUtils.UseTestTraceFmt();
        try
        {
            ETLProcessor x = new ETLProcessor();
            x.OpenFile(ETWTestUtils.GetSampleETLFile());
            x.DoPreProcessing();
            //x.LoadInMemory();
            List<ISearchResult> blah = x.GetResults();
            Assert.IsTrue(blah.Count() > 100);
            x.Dispose();
            Assert.IsTrue(true);
        }
        catch (Exception e)
        {
            if (e.ToString().Contains("Cant find tracefmt"))
            {
                Assert.Fail("Cant find tracefmt :("); //We have a testhook for it now
            }
            else
            {
                Assert.Fail("Should not throw" + e);
            }
        }
    }

    [TestMethod]
    public void CanProcessVeryLargeLogFile()
    {
        // Arrange
        string tempFile = Path.GetTempFileName();
        string largeFile = Path.ChangeExtension(tempFile, ".log");
        File.Move(tempFile, largeFile);

        // Generate a 100MB file with valid ETLLogLine headers that pass DoesHeaderLookRight
        // Format: [0]1234.::5678 2024-01-01 00:00:00 [TestProvider]{}
        string line = "[0]1234.::5678 2024-01-01 00:00:00 [TestProvider]{}\n";
        int lineSize = Encoding.UTF8.GetByteCount(line);
        int lines = (int)(200 * 1024 * 1024 / lineSize);
        using (var fs = new FileStream(largeFile, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var sw = new StreamWriter(fs, Encoding.UTF8))
        {
            for (int i = 0; i < lines; i++)
            {
                sw.Write(line);
            }
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            ETLProcessor processor = new ETLProcessor();
            processor.OpenFile(largeFile);
            processor.DoPreProcessing();
            processor.LoadInMemory();
            var results = processor.GetResults();
            stopwatch.Stop();
            Assert.IsNotNull(results);
            Assert.IsTrue(results.Count > 0); // Should have processed lines
            Assert.IsTrue(stopwatch.Elapsed.TotalSeconds < 60, $"Processing took too long: {stopwatch.Elapsed.TotalSeconds} seconds");
        }
        finally
        {
            if (File.Exists(largeFile))
                File.Delete(largeFile);
        }
    }

    [TestMethod]
    public void CanProcessVeryLargeHeavyJsonLogFile()
    {
        // Arrange
        string tempFile = Path.GetTempFileName();
        string largeFile = Path.ChangeExtension(tempFile, ".log");
        File.Move(tempFile, largeFile);

        // Generate a 50MB file with valid ETLLogLine headers and heavy JSON payloads
        // JSON includes meta fields and extra fields to exercise PreLoad
        string json = "{\"meta\":{\"provider\":\"TestProvider\",\"time\":\"2024-01-01T00:00:00\",\"event\":\"TestEvent\",\"task\":\"TestTask\",\"level\":2},\"extra1\":\"value1\",\"extra2\":123,\"extra3\":true}";
        string line = $"[0]1234.::5678 2024-01-01 00:00:00 [TestProvider]{json}\n";
        int lineSize = Encoding.UTF8.GetByteCount(line);
        int lines = (int)(200 * 1024 * 1024 / lineSize); // 50MB for speed
        using (var fs = new FileStream(largeFile, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var sw = new StreamWriter(fs, Encoding.UTF8))
        {
            for (int i = 0; i < lines; i++)
            {
                sw.Write(line);
            }
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            ETLProcessor processor = new ETLProcessor();
            processor.OpenFile(largeFile);
            processor.DoPreProcessing();
            processor.LoadInMemory();
            var results = processor.GetResults();
            stopwatch.Stop();
            Assert.IsNotNull(results);
            Assert.IsTrue(results.Count > 0); // Should have processed lines
            // Check that PreLoad parsed the JSON and set fields
            var first = results[0] as ETLLogLine;
            Assert.IsNotNull(first);
            Assert.AreEqual("TestTask", first.tasktxt);
            Assert.AreEqual("TestEvent", first.eventtxt);
            Assert.AreEqual("TestProvider", first.metaprovider);
            Assert.AreEqual(Level.Error, first.GetLevel());
            Assert.IsTrue(stopwatch.Elapsed.TotalSeconds < 60, $"Processing took too long: {stopwatch.Elapsed.TotalSeconds} seconds");
        }
        finally
        {
            if (File.Exists(largeFile))
                File.Delete(largeFile);
        }
    }

    }