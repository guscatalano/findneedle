using System;
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

    // ----- tags ride along with an export -----

    private static Dictionary<long, (string Name, string Note)> Tags(params (long id, string name, string note)[] t)
        => t.ToDictionary(x => x.id, x => (x.name, x.note));

    [TestMethod]
    public void Csv_CarriesTheTagColumns_WhenRowsAreTagged()
    {
        using var src = Source(new FakeResult("first"), new FakeResult("second"));
        var lines = ResultExporter.BuildLines(src, FilterSpec.Empty, SortSpec.None, Cols,
            ResultExporter.Format.Csv, out _, Tags((1, "Cause", "the reset lands here")));

        Assert.AreEqual("Index,Message,Level,Tag,Tag note", lines[0], "tag columns are appended, not substituted");
        StringAssert.EndsWith(lines[1], ",,", "an untagged row leaves them empty");
        StringAssert.EndsWith(lines[2], ",Cause,the reset lands here");
    }

    [TestMethod]
    public void Export_IsUnchanged_WhenNothingIsTagged()
    {
        using var src = Source(new FakeResult("first"));
        var withEmpty = ResultExporter.BuildLines(src, FilterSpec.Empty, SortSpec.None, Cols,
            ResultExporter.Format.Csv, out _, new Dictionary<long, (string, string)>());
        Assert.AreEqual("Index,Message,Level", withEmpty[0], "no tags, no extra columns");
    }

    [TestMethod]
    public void Json_And_Xml_CarryTags_WithAnElementNameThatIsValidXml()
    {
        using var src = Source(new FakeResult("only"));
        var tags = Tags((0, "Note", "look here"));

        var json = string.Join(Environment.NewLine, ResultExporter.BuildLines(src, FilterSpec.Empty, SortSpec.None, Cols,
            ResultExporter.Format.Json, out _, tags));
        StringAssert.Contains(json, "\"Tag\":\"Note\"");
        StringAssert.Contains(json, "\"Tag note\":\"look here\"");

        var xml = string.Join(Environment.NewLine, ResultExporter.BuildLines(src, FilterSpec.Empty, SortSpec.None, Cols,
            ResultExporter.Format.Xml, out _, tags));
        StringAssert.Contains(xml, "<Tag>Note</Tag>");
        StringAssert.Contains(xml, "<TagNote>look here</TagNote>", "a space is not legal in an element name");
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
