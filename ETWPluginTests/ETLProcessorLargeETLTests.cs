using System;
using System.Diagnostics;
using System.IO;
using findneedle.Implementations.FileExtensions;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;

namespace ETWPluginTests;

[TestClass]
public class ETLProcessorLargeETLTests
{
    [TestMethod]
    public void CanProcessVeryLargeSampleETLFile()
    {
        // Use the provided large ETL file
        string sampleEtl = @"C:\Users\crimson\Desktop\samplelogs\test1.etl";
        
        if (!File.Exists(sampleEtl))
        {
            Assert.Inconclusive($"Sample ETL file does not exist: {sampleEtl}");
            return;
        }
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
