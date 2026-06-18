using System.Collections.Generic;
using System.Linq;
using FindNeedlePluginLib;
using FindNeedleUX;
using FindNeedleUX.Services;
using FindNeedleUX.Services.PagedLogSource;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Mcp;

/// <summary>
/// Unit tests for the headless <see cref="ResultExporter"/> shared by the viewer export and the MCP
/// export tool. Drives an in-memory paged source so no SQLite/UI is needed.
/// </summary>
[TestClass]
[TestCategory("Mcp")]
public class ResultExporterTests
{
    private static InMemoryPagedSource Source(params ISearchResult[] rows)
        => new(rows.Select((r, i) => new LogLine(r, i)).ToList());

    private static readonly string[] Cols = { "Index", "Message", "Level" };

    [TestMethod]
    public void Csv_HasHeaderRowsAndEscaping()
    {
        using var src = Source(
            new FakeResult("plain", level: Level.Error),
            new FakeResult("a,b\"c", level: Level.Info)); // comma + quote → must be CSV-escaped

        var lines = ResultExporter.BuildLines(src, FilterSpec.Empty, SortSpec.None, Cols, ResultExporter.Format.Csv, out var count);

        Assert.AreEqual(2, count, "two data rows");
        Assert.AreEqual("Index,Message,Level", lines[0], "header is the visible columns");
        Assert.AreEqual(3, lines.Count, "header + 2 rows");
        Assert.IsTrue(lines[2].Contains("\"a,b\"\"c\""), "comma/quote value is quoted + doubled: " + lines[2]);
    }

    [TestMethod]
    public void Json_IsArrayWithOneObjectPerRow()
    {
        using var src = Source(new FakeResult("m1"), new FakeResult("m2"), new FakeResult("m3"));

        var lines = ResultExporter.BuildLines(src, FilterSpec.Empty, SortSpec.None, Cols, ResultExporter.Format.Json, out var count);

        Assert.AreEqual(3, count);
        Assert.AreEqual("[", lines[0]);
        Assert.AreEqual("]", lines[^1]);
        Assert.AreEqual(5, lines.Count, "[ + 3 rows + ]");
        Assert.IsTrue(lines[1].Contains("\"Message\""), "rows are JSON objects keyed by column");
    }

    [TestMethod]
    public void Xml_WrapsRowsElement()
    {
        using var src = Source(new FakeResult("m1"), new FakeResult("m2"));

        var lines = ResultExporter.BuildLines(src, FilterSpec.Empty, SortSpec.None, Cols, ResultExporter.Format.Xml, out var count);

        Assert.AreEqual(2, count);
        CollectionAssert.Contains(lines, "<rows>");
        CollectionAssert.Contains(lines, "</rows>");
        Assert.IsTrue(lines.Any(l => l.Contains("<row>") && l.Contains("<Message>")), "each row is an XML element");
    }

    [TestMethod]
    public void RespectsFilter_OnlyMatchingRowsExported()
    {
        using var src = Source(
            new FakeResult("keep me", level: Level.Error),
            new FakeResult("drop me", level: Level.Info));
        var onlyError = new FilterSpec("", "", "", "", "", Level.Error.ToString(), null, null);

        ResultExporter.BuildLines(src, onlyError, SortSpec.None, Cols, ResultExporter.Format.Csv, out var count);

        Assert.AreEqual(1, count, "only the Error row is exported");
    }
}
