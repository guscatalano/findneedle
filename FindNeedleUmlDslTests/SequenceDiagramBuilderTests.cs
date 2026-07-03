using System;
using System.Collections.Generic;
using FindNeedleUmlDsl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUmlDslTests;

[TestClass]
public class SequenceDiagramBuilderTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static SequenceDiagramBuilder.Interaction Ev(string who, string what, int sec)
        => new(who, what, T0.AddSeconds(sec));

    [TestMethod]
    public void Build_OrdersByTime_ChainsArrowsBetweenActors()
    {
        // Fed out of order; must come out in time order as a note then arrows.
        var mmd = SequenceDiagramBuilder.BuildMermaid(new[]
        {
            Ev("B", "step3", 3),
            Ev("A", "step1", 1),
            Ev("A", "step2", 2),
        });

        StringAssert.StartsWith(mmd, "sequenceDiagram");
        StringAssert.Contains(mmd, "participant p0 as A");
        StringAssert.Contains(mmd, "participant p1 as B");
        // First (earliest) event is a note on its actor; later events are arrows.
        StringAssert.Contains(mmd, "Note over p0:");     // A/step1
        StringAssert.Contains(mmd, "p0->>p0:");          // A/step2 (self)
        StringAssert.Contains(mmd, "p0->>p1:");          // A→B/step3
        // Time order preserved: step1 before step3 in the text.
        Assert.IsTrue(mmd.IndexOf("step1", StringComparison.Ordinal) < mmd.IndexOf("step3", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Build_CollapsesConsecutiveIdentical_WithCount()
    {
        var mmd = SequenceDiagramBuilder.BuildMermaid(new[]
        {
            Ev("A", "poll", 1),
            Ev("A", "poll", 2),
            Ev("A", "poll", 3),
        });
        StringAssert.Contains(mmd, "(×3)");
    }

    [TestMethod]
    public void Build_ReturnsNull_WhenNothingUsable()
    {
        Assert.IsNull(SequenceDiagramBuilder.BuildMermaid(null));
        Assert.IsNull(SequenceDiagramBuilder.BuildMermaid(Array.Empty<SequenceDiagramBuilder.Interaction>()));
        Assert.IsNull(SequenceDiagramBuilder.BuildMermaid(new[] { Ev("  ", "x", 1) })); // blank participant
    }

    [TestMethod]
    public void Build_SanitizesParticipantNames()
    {
        var mmd = SequenceDiagramBuilder.BuildMermaid(new[] { Ev("Weird;Name\nhere", "x", 1) });
        // Semicolon + newline become spaces so Mermaid can parse the participant line.
        StringAssert.Contains(mmd, "participant p0 as Weird Name here");
        Assert.IsFalse(mmd.Contains(";Name"), "semicolon should be sanitized out of the name");
    }

    [TestMethod]
    public void Build_CapsArrows_WithTruncationNote()
    {
        var events = new List<SequenceDiagramBuilder.Interaction>();
        for (int i = 0; i < 300; i++) events.Add(Ev(i % 2 == 0 ? "A" : "B", "e" + i, i));
        var mmd = SequenceDiagramBuilder.BuildMermaid(events, maxArrows: 50);
        StringAssert.Contains(mmd, "truncated");
    }
}
