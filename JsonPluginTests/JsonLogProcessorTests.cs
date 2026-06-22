using System;
using System.Collections.Generic;
using System.IO;
using FindNeedlePluginLib;
using JsonPlugin;

namespace JsonPluginTests;

[TestClass]
public class JsonLogProcessorTests
{
    private static string Sample(string name) =>
        Path.Combine(AppContext.BaseDirectory, "SampleFiles", name);

    private static List<ISearchResult> Load(string path)
    {
        var p = new JsonLogProcessor();
        p.OpenFile(path);
        p.LoadInMemory();
        return p.GetResults();
    }

    [TestMethod]
    public void RegistersForJsonExtensions()
    {
        var ext = new JsonLogProcessor().RegisterForExtensions();
        CollectionAssert.Contains(ext, ".json");
        CollectionAssert.Contains(ext, ".jsonl");
        CollectionAssert.Contains(ext, ".ndjson");
    }

    [TestMethod]
    public void Jsonl_ParsesAndMaps()
    {
        var rows = Load(Sample("sample.jsonl"));
        Assert.AreEqual(2, rows.Count);
        var r = rows[0];
        Assert.AreEqual(Level.Info, r.GetLevel());          // "Information" → Info
        Assert.AreEqual("Auth", r.GetSource());             // logger → Provider column
        Assert.AreEqual("1234", r.GetThreadId());           // numeric JSON value
        Assert.AreEqual("User logged in", r.GetMessage());
        Assert.AreEqual(2026, r.GetLogTime().Year);
        StringAssert.Contains(r.GetSearchableData(), "US"); // unmapped "region"
    }

    [TestMethod]
    public void JsonArray_Parses()
    {
        var rows = Load(Sample("sample.json"));
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(Level.Warning, rows[0].GetLevel()); // severity → level
        Assert.AreEqual("Db", rows[0].GetSource());         // source → Provider
        Assert.AreEqual("Slow query", rows[0].GetMessage());
    }

    [TestMethod]
    public void Clef_MapsAtFields()
    {
        var rows = Load(Sample("clef.jsonl"));
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual(Level.Error, rows[0].GetLevel());        // @l
        Assert.AreEqual("Order {OrderId} failed", rows[0].GetMessage()); // @mt
        Assert.AreEqual(2026, rows[0].GetLogTime().Year);        // @t
        Assert.AreEqual("All good", rows[1].GetMessage());       // @m
        Assert.AreEqual(Level.Info, rows[1].GetLevel());         // no @l → default Info
    }

    [TestMethod]
    public void MalformedLineIsSkipped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"jsontest_{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path,
            "{\"level\":\"Info\",\"message\":\"good\"}\n" +
            "{ this is not json }\n" +
            "{\"level\":\"Error\",\"message\":\"also good\"}\n");
        try
        {
            var rows = Load(path);
            Assert.AreEqual(2, rows.Count, "The malformed middle line should be skipped, not abort the file.");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void CheckFileFormat_TrueForJson_FalseForBinary()
    {
        var p = new JsonLogProcessor();
        p.OpenFile(Sample("sample.jsonl"));
        Assert.IsTrue(p.CheckFileFormat());

        var bin = Path.Combine(Path.GetTempPath(), $"jsontest_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(bin, new byte[] { 0x00, 0x01, 0x02, 0x00, 0xFF });
        try
        {
            var p2 = new JsonLogProcessor();
            p2.OpenFile(bin);
            Assert.IsFalse(p2.CheckFileFormat());
        }
        finally { File.Delete(bin); }
    }
}
