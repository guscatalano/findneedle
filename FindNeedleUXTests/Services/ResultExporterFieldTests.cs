using System;
using FindNeedleUX;
using FindNeedleUX.Services;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Services;

/// <summary>
/// Tests for <see cref="ResultExporter.GetField"/> (column-name → row value) and
/// <see cref="ResultExporter.EscapeCsv"/> (RFC-4180 cell escaping) — the per-cell pieces behind the
/// CSV/JSON/XML export (the format-level output is covered by ResultExporterTests).
/// </summary>
[TestClass]
[TestCategory("Services")]
public class ResultExporterFieldTests
{
    [TestMethod]
    public void GetField_ReturnsEachMappedColumn()
    {
        var line = new LogLine(new R(), 7);
        Assert.AreEqual("7", ResultExporter.GetField(line, "Index"));
        Assert.AreEqual("Auth", ResultExporter.GetField(line, "Provider"));
        Assert.AreEqual("Logon", ResultExporter.GetField(line, "TaskName"));
        Assert.AreEqual("hello", ResultExporter.GetField(line, "Message"));
        Assert.AreEqual("Info", ResultExporter.GetField(line, "Level"));
        Assert.AreEqual(line.Time, ResultExporter.GetField(line, "Time"));
        Assert.AreEqual(line.Source, ResultExporter.GetField(line, "Source"));
    }

    [TestMethod]
    public void GetField_UnknownColumn_ReturnsEmpty()
        => Assert.AreEqual("", ResultExporter.GetField(new LogLine(new R(), 0), "NoSuchColumn"));

    [TestMethod]
    public void EscapeCsv_QuotesWhenNeeded()
    {
        Assert.AreEqual("plain", ResultExporter.EscapeCsv("plain"));
        Assert.AreEqual("\"a,b\"", ResultExporter.EscapeCsv("a,b"));
        Assert.AreEqual("\"he said \"\"hi\"\"\"", ResultExporter.EscapeCsv("he said \"hi\""));
        Assert.AreEqual("\"line1\nline2\"", ResultExporter.EscapeCsv("line1\nline2"));
        Assert.AreEqual("", ResultExporter.EscapeCsv(""));
        Assert.AreEqual("", ResultExporter.EscapeCsv(null));
    }

    private sealed class R : ISearchResult
    {
        public DateTime GetLogTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => "u";
        public string GetTaskName() => "Logon";
        public string GetOpCode() => "";
        public string GetSource() => "Auth";
        public string GetSearchableData() => "hello";
        public string GetMessage() => "hello";
        public string GetResultSource() => @"C:\logs\a.log";
    }
}
