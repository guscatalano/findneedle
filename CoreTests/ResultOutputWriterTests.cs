using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FindNeedlePluginLib;
using FindPluginCore.Output;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>Covers the CLI's --out serializers (csv/json/txt/html): escaping, structure, round-trip.</summary>
[TestClass]
public sealed class ResultOutputWriterTests
{
    private sealed class FakeResult : ISearchResult
    {
        public DateTime Time = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        public Level Lvl = Level.Info;
        public string Pid = "", Tid = "", Src = "", Task = "", Op = "", Msg = "";
        public DateTime GetLogTime() => Time;
        public string GetMachineName() => "";
        public void WriteToConsole() { }
        public Level GetLevel() => Lvl;
        public string GetUsername() => "";
        public string GetTaskName() => Task;
        public string GetOpCode() => Op;
        public string GetSource() => Src;
        public string GetSearchableData() => Msg;
        public string GetMessage() => Msg;
        public string GetResultSource() => "test";
        public string GetProcessId() => Pid;
        public string GetThreadId() => Tid;
    }

    private static List<ISearchResult> Sample() => new()
    {
        new FakeResult { Src = "AuthService", Task = "Login", Msg = "user=alice ok", Pid = "1A40", Tid = "2001" },
        new FakeResult { Lvl = Level.Error, Src = "DbPool", Msg = "query, with \"comma\" and quote", Pid = "1A43" },
    };

    [TestMethod]
    public void Csv_HasHeaderAndEscapesCommasAndQuotes()
    {
        var csv = ResultOutputWriter.ToCsv(Sample());
        var lines = csv.Replace("\r", "").TrimEnd('\n').Split('\n');
        Assert.AreEqual(3, lines.Length, "header + 2 rows");
        StringAssert.StartsWith(lines[0], "Time,Level,PID,TID,Source,Task,OpCode,Message");
        // The comma+quote message must be wrapped and its quotes doubled.
        StringAssert.Contains(lines[2], "\"query, with \"\"comma\"\" and quote\"");
    }

    [TestMethod]
    public void Json_IsValidArray_PreservesUnicode()
    {
        var rows = new List<ISearchResult> { new FakeResult { Msg = "検索 test", Src = "S" } };
        var json = ResultOutputWriter.ToJson(rows);
        using var doc = JsonDocument.Parse(json); // valid JSON
        Assert.AreEqual(1, doc.RootElement.GetArrayLength());
        Assert.AreEqual("検索 test", doc.RootElement[0].GetProperty("Message").GetString());
    }

    [TestMethod]
    public void Txt_OneLinePerRow_WithMessage()
    {
        var txt = ResultOutputWriter.ToTxt(Sample());
        var lines = txt.Replace("\r", "").TrimEnd('\n').Split('\n');
        Assert.AreEqual(2, lines.Length);
        StringAssert.Contains(lines[0], "user=alice ok");
        StringAssert.Contains(lines[0], "AuthService");
    }

    [TestMethod]
    public void Html_HasTable_AndEscapesMarkup()
    {
        var rows = new List<ISearchResult> { new FakeResult { Msg = "<script>alert(1)</script>", Lvl = Level.Error } };
        var html = ResultOutputWriter.ToHtml(rows);
        StringAssert.Contains(html, "<table>");
        StringAssert.Contains(html, "&lt;script&gt;");           // escaped, not raw
        Assert.IsFalse(html.Contains("<script>alert"), "raw markup must not leak into the HTML");
        StringAssert.Contains(html, "class=\"Error\"");          // level class for styling
    }

    [TestMethod]
    public void Serialize_UnknownFormat_Throws()
        => Assert.ThrowsException<ArgumentException>(() => ResultOutputWriter.Serialize(Sample(), "xml"));

    [TestMethod]
    public void WriteToFile_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fn_out_{Guid.NewGuid():N}.csv");
        try
        {
            var written = ResultOutputWriter.WriteToFile(Sample(), "csv", path);
            Assert.AreEqual(path, written);
            Assert.IsTrue(File.Exists(path));
            StringAssert.Contains(File.ReadAllText(path), "AuthService");
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [TestMethod]
    public void IsSupported_And_Extension()
    {
        Assert.IsTrue(ResultOutputWriter.IsSupported("JSON"));   // case-insensitive
        Assert.IsFalse(ResultOutputWriter.IsSupported("xml"));
        Assert.AreEqual(".html", ResultOutputWriter.ExtensionFor("HTML"));
    }
}
