using System;
using System.Collections.Generic;
using System.Linq;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUX.UnitTests.Services;

/// <summary>
/// "Export tagged rows…" - the Markdown timeline a triage pass ends in. Pure function, so the shape
/// of the writeup is pinned here rather than eyeballed in a file dialog.
/// </summary>
[TestClass]
public class TagTimelineTests
{
    private static readonly DateTime T0 = new(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc);

    private static ResultExporter.TaggedRow Row(double secondsAfter, string tag, string note = "",
                                                string message = "something happened", string level = "Error") =>
        new(T0.AddSeconds(secondsAfter), tag, note, level, "Driver", "Reset", @"C:\logs\app.etl", message);

    private static string Render(params ResultExporter.TaggedRow[] rows)
        => string.Join("\n", ResultExporter.BuildTagTimeline(rows, "Tagged rows - repro 3"));

    [TestMethod]
    public void TheTimeline_LeadsWithWhatItIsAndHowLongItCovers()
    {
        var md = Render(Row(0, "Cause"), Row(2.14, "Effect"));
        StringAssert.StartsWith(md, "# Tagged rows - repro 3");
        StringAssert.Contains(md, "2 tagged rows spanning 2.1 s");
        StringAssert.Contains(md, "2026-03-01 09:30:00.000");
        StringAssert.Contains(md, "Tags: ");
    }

    [TestMethod]
    public void EveryRow_IsPlacedRelativeToTheFirstOne()
    {
        var md = Render(Row(0, "Cause"), Row(2.14, "Effect"));
        StringAssert.Contains(md, "## +00:00.000 - Cause");
        StringAssert.Contains(md, "## +00:02.140 - Effect");
    }

    [TestMethod]
    public void RowsComeOutInTimeOrder_WhateverOrderTheyWereTaggedIn()
    {
        var md = Render(Row(9, "Third"), Row(0, "First"), Row(3, "Second"));
        var order = new[] { md.IndexOf("First", StringComparison.Ordinal),
                            md.IndexOf("Second", StringComparison.Ordinal),
                            md.IndexOf("Third", StringComparison.Ordinal) };
        CollectionAssert.AreEqual(order.OrderBy(x => x).ToArray(), order, "the timeline is the log's order, not the tagging order");
    }

    [TestMethod]
    public void AnEntry_CarriesTheNote_TheFacts_AndTheMessage()
    {
        var md = Render(Row(0, "Cause", "this is where the reset starts", "Driver reset requested"));
        StringAssert.Contains(md, "this is where the reset starts");
        StringAssert.Contains(md, "`09:30:00.0000000 | Error | Driver / Reset | app.etl`");
        StringAssert.Contains(md, "> Driver reset requested");
    }

    [TestMethod]
    public void AMultiLineMessage_StaysInsideTheQuote()
    {
        var md = Render(Row(0, "Note", message: "first line\r\nsecond line"));
        StringAssert.Contains(md, "> first line");
        StringAssert.Contains(md, "> second line");
    }

    [TestMethod]
    public void AnUntaggedCategoryReadsAsNone_AndAnEmptyNoteIsJustOmitted()
    {
        var md = Render(Row(0, "", ""));
        StringAssert.Contains(md, "## +00:00.000 - (none)");
        Assert.IsFalse(md.Contains("\n\n\n"), "no hole where the note would have been: " + md);
    }

    [TestMethod]
    public void ALongPass_UsesHoursInTheOffsets()
    {
        var md = Render(Row(0, "Start"), Row(3 * 60 * 60 + 5, "Much later"));
        StringAssert.Contains(md, "spanning 3.0 h");
        StringAssert.Contains(md, "## +3:00:05.000 - Much later");
    }

    [TestMethod]
    public void NoTaggedRows_SaysSo_RatherThanProducingAnEmptyFile()
    {
        var lines = ResultExporter.BuildTagTimeline(Array.Empty<ResultExporter.TaggedRow>());
        Assert.AreEqual("# Tagged rows", lines[0]);
        CollectionAssert.Contains(lines, "_No tagged rows._");
    }

    [TestMethod]
    public void OneRow_ReadsAsOneRow()
    {
        var md = Render(Row(0, "Cause"));
        StringAssert.Contains(md, "1 tagged row spanning 0 ms");
    }
}
