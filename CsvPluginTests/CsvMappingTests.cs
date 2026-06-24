using System;
using System.Collections.Generic;
using System.IO;
using FindNeedlePluginLib;
using FindNeedlePluginUtils.StructuredLog;

namespace CsvPluginTests;

[TestClass]
public class CsvMappingTests
{
    private static KeyValuePair<string, string> F(string k, string v) => new(k, v);

    [TestMethod]
    public void Override_BeatsAlias_AndMapsUnaliasedHeader()
    {
        var fields = new List<KeyValuePair<string, string>> { F("col1", "MyProvider"), F("col2", "hi") };
        var overrides = new Dictionary<string, string> { ["col1"] = "provider" };
        var r = StructuredLogFieldMapper.Map(fields, "f.csv", 1, overrides);
        Assert.AreEqual("MyProvider", r.GetSource()); // GetSource() feeds the Provider column
    }

    [TestMethod]
    public void Override_DataOnly_LeavesFieldUnmapped()
    {
        var fields = new List<KeyValuePair<string, string>> { F("provider", "X") };
        var overrides = new Dictionary<string, string> { ["provider"] = "-" };
        var r = StructuredLogFieldMapper.Map(fields, "f.csv", 1, overrides);
        Assert.AreEqual(string.Empty, r.Provider); // not mapped despite the "provider" header
    }

    [TestMethod]
    public void AbsentHeader_FallsBackToAlias()
    {
        var fields = new List<KeyValuePair<string, string>> { F("level", "Error"), F("msg", "boom") };
        var overrides = new Dictionary<string, string> { ["something"] = "time" };
        var r = StructuredLogFieldMapper.Map(fields, "f.csv", 1, overrides);
        Assert.AreEqual(Level.Error, r.GetLevel());  // alias still applies for headers absent from overrides
        Assert.AreEqual("boom", r.GetMessage());
    }

    [TestMethod]
    public void NullOverrides_BehavesLikeAliasOnly()
    {
        var fields = new List<KeyValuePair<string, string>> { F("provider", "P") };
        var r = StructuredLogFieldMapper.Map(fields, "f.csv", 1, null);
        Assert.AreEqual("P", r.GetSource());
    }

    [TestMethod]
    public void Store_RoundTrips_KeyedByColumnSet_OrderSensitive()
    {
        var dir = Path.Combine(Path.GetTempPath(), "fn_csvmap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "csv-mappings.json");
        try
        {
            CsvColumnMappingStore.SetStorageLocationForTests(file);

            var headers = new[] { "a", "B", "c" };
            Assert.IsFalse(CsvColumnMappingStore.Has(headers));
            CsvColumnMappingStore.Set(headers, new Dictionary<string, string> { ["a"] = "time", ["c"] = "message" });
            Assert.IsTrue(CsvColumnMappingStore.Has(headers));

            // Key is case-insensitive (B vs b) but order-sensitive.
            var got = CsvColumnMappingStore.Get(new[] { "a", "b", "c" });
            Assert.IsNotNull(got);
            Assert.AreEqual("time", got["a"]);
            Assert.AreEqual("message", got["c"]);
            Assert.IsFalse(CsvColumnMappingStore.Has(new[] { "a", "c", "B" }), "different column order is a different set");
        }
        finally
        {
            CsvColumnMappingStore.ResetStorageForTests();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
