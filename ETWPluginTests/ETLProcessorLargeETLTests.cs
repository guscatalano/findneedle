using System;
using System.Diagnostics;
using System.IO;
using findneedle.Implementations.FileExtensions;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using TestUtilities.Helpers;

namespace ETWPluginTests;

[TestClass]
public class ETLProcessorLargeETLTests
{
    // Resolve a large .etl to exercise the eager GetResults() materialization path (the legacy/CLI path).
    // Prefer FINDNEEDLE_ETL, else the repo's portable LargeSamples\large-5M.etl, so this runs as part of
    // the local suite instead of depending on a personal file. Inconclusive if none is present.
    private static string ResolveSampleEtl()
    {
        var env = Environment.GetEnvironmentVariable("FINDNEEDLE_ETL");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return env;
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            var cand = Path.Combine(dir, "LargeSamples", "large-5M.etl");
            if (File.Exists(cand)) return cand;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    private static readonly string SampleEtlPath = ResolveSampleEtl();
    private TestContext? _testContext;

    public TestContext TestContext
    {
        get => _testContext ?? throw new InvalidOperationException("TestContext not initialized");
        set => _testContext = value;
    }

    [TestInitialize]
    public void TestInitialize()
    {
        PerformanceTestInitializer.CheckSystemRequirements(TestContext);

        if (!File.Exists(SampleEtlPath))
        {
            throw new AssertInconclusiveException($"Sample ETL file does not exist: {SampleEtlPath}. Skipping large file tests.");
        }
    }

    [TestMethod]
    [RequiresMinimumSpecs(MinimumRamGb = 2, MinimumProcessorCount = 2, 
        Reason = "Large ETL file processing requires adequate system resources")]
    public void CanProcessVeryLargeSampleETLFile()
    {
        // Use the provided large ETL file
        string sampleEtl = SampleEtlPath;
        var fileInfo = new FileInfo(sampleEtl);
        Assert.IsTrue(fileInfo.Length > 1024 * 100, "Sample ETL file is too small to be a valid test");



        var stopwatch = Stopwatch.StartNew();
        var stepOpen = Stopwatch.StartNew();
        var processor = new ETLProcessor();
        processor.OpenFile(sampleEtl);
        stepOpen.Stop();
        Console.WriteLine($"OpenFile took {stepOpen.Elapsed.TotalSeconds:F2} seconds");

        var stepPre = Stopwatch.StartNew();
        processor.DoPreProcessing();
        stepPre.Stop();
        Console.WriteLine($"DoPreProcessing took {stepPre.Elapsed.TotalSeconds:F2} seconds");

        var stepLoad = Stopwatch.StartNew();
        processor.LoadInMemory();
        stepLoad.Stop();
        Console.WriteLine($"LoadInMemory took {stepLoad.Elapsed.TotalSeconds:F2} seconds");

        var stepResults = Stopwatch.StartNew();
        var results = processor.GetResults();
        stepResults.Stop();
        Console.WriteLine($"GetResults took {stepResults.Elapsed.TotalSeconds:F2} seconds");

        stopwatch.Stop();

        // Assert
        Assert.IsNotNull(results);
        Assert.IsTrue(results.Count > 0, "No results parsed from large ETL");
        Console.WriteLine($"Total processing large ETL file took {stopwatch.Elapsed.TotalSeconds:F2} seconds");
    }
}
