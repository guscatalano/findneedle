using System;
using System.IO;
using System.Linq;
using CsvPlugin;
using FindNeedlePluginLib;

namespace CsvPluginTests;

[TestClass]
public class CsvLogProcessorTests
{
    private static string SamplePath =>
        Path.Combine(AppContext.BaseDirectory, "SampleFiles", "sample.csv");

    private static System.Collections.Generic.List<ISearchResult> Load(string path)
    {
        var p = new CsvLogProcessor();
        p.OpenFile(path);
        p.LoadInMemory();
        return p.GetResults();
    }

    [TestMethod]
    public void RegistersForCsvAndTsv()
    {
        var ext = new CsvLogProcessor().RegisterForExtensions();
        CollectionAssert.Contains(ext, ".csv");
        CollectionAssert.Contains(ext, ".tsv");
    }

    [TestMethod]
    public void ParsesEveryDataRow()
    {
        var rows = Load(SamplePath);
        Assert.AreEqual(3, rows.Count, "Header should not be a row; 3 data rows expected.");
    }

    [TestMethod]
    public void MapsWellKnownColumns()
    {
        var r = Load(SamplePath)[0];
        Assert.AreEqual(Level.Info, r.GetLevel());
        Assert.AreEqual("Auth", r.GetSource());        // Provider column → GetSource
        Assert.AreEqual("1234", r.GetThreadId());
        Assert.AreEqual("User logged in", r.GetMessage());
        Assert.AreEqual(2026, r.GetLogTime().Year);
    }

    [TestMethod]
    public void HonorsQuotedFieldWithEmbeddedComma()
    {
        var r = Load(SamplePath)[1];
        Assert.AreEqual("Login failed, retrying", r.GetMessage());
        Assert.AreEqual(Level.Error, r.GetLevel());
    }

    [TestMethod]
    public void UnmappedColumnIsSearchableAndStructured()
    {
        var r = Load(SamplePath)[0];
        StringAssert.Contains(r.GetSearchableData(), "US");       // the unmapped "Region" value
        StringAssert.Contains(r.GetStructuredData(), "Region");   // preserved in the JSON payload
    }

    [TestMethod]
    public void CheckFileFormat_TrueForCsv_FalseForBinary()
    {
        var p = new CsvLogProcessor();
        p.OpenFile(SamplePath);
        Assert.IsTrue(p.CheckFileFormat());

        var bin = Path.Combine(Path.GetTempPath(), $"csvtest_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(bin, new byte[] { 0x00, 0x01, 0x02, 0x00, 0xFF });
        try
        {
            var p2 = new CsvLogProcessor();
            p2.OpenFile(bin);
            Assert.IsFalse(p2.CheckFileFormat());
        }
        finally { File.Delete(bin); }
    }
}
