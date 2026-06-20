using System.IO;
using findneedle.Implementations;
using findneedle.Implementations.FileExtensions;
using FindNeedlePluginLib;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;

namespace ETWPluginTests;

/// <summary>
/// Loads a folder that contains two .etl files through the full pipeline (FolderLocation + ETLProcessor
/// -> NuSearchQuery). Guards the multi-file fix: FolderLocation must process each file with its own
/// processor instance, so both files load completely (no rows lost or double-counted from a shared,
/// stateful processor) and results from both files are present.
/// </summary>
[TestClass]
public sealed class TwoEtlsInFolderTests
{
    public TestContext TestContext { get; set; } = null!;

    private static int LoadCount(string path, out System.Collections.Generic.HashSet<string> sources)
    {
        var loc = new FolderLocation { path = path };
        loc.SetExtensionProcessorList(new System.Collections.Generic.List<IFileExtensionProcessor> { new ETLProcessor() });
        var query = new NuSearchQuery { OverrideStorageType = StorageType.InMemory };
        query.Locations.Add(loc);
        query.RunThrough();

        var rows = new System.Collections.Generic.List<ISearchResult>();
        query.ResultStorage!.GetFilteredResultsInBatches(b => rows.AddRange(b));
        sources = new System.Collections.Generic.HashSet<string>(
            rows.Select(r => r.GetResultSource()), System.StringComparer.OrdinalIgnoreCase);
        int count = rows.Count;
        query.DisposeStorage();
        return count;
    }

    [TestMethod]
    public void FolderWithTwoEtls_LoadsBothFiles()
    {
        ETWTestUtils.UseTestTraceFmt();
        var sample = ETWTestUtils.GetSampleETLFile();

        // Baseline: one .etl on its own.
        int single = LoadCount(sample, out _);
        Assert.IsTrue(single > 0, "the sample .etl should decode to at least one row");
        TestContext.WriteLine($"single .etl rows: {single}");

        // A folder holding two copies of the .etl.
        var dir = Path.Combine(Path.GetTempPath(), $"FN_twoetl_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.Copy(sample, Path.Combine(dir, "trace_a.etl"));
            File.Copy(sample, Path.Combine(dir, "trace_b.etl"));

            int both = LoadCount(dir, out var sources);
            TestContext.WriteLine($"folder (2 .etl) rows: {both}; sources: {string.Join(", ", sources)}");

            // Both files fully loaded — exactly twice the single-file count (no lost/duplicated rows).
            Assert.AreEqual(single * 2, both, "two identical .etls should yield exactly twice the rows");
            // Results from both files are present (per-file processor instances kept their own filename).
            Assert.IsTrue(sources.Any(s => s.Contains("trace_a.etl", System.StringComparison.OrdinalIgnoreCase)),
                "results from trace_a.etl should be present");
            Assert.IsTrue(sources.Any(s => s.Contains("trace_b.etl", System.StringComparison.OrdinalIgnoreCase)),
                "results from trace_b.etl should be present");
        }
        finally
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
