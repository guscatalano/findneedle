using System.Collections.Generic;
using System.Linq;
using FindNeedleUmlDsl;
using FindNeedleUmlDsl.MermaidUML;

namespace FindNeedleUmlDslTests;

/// <summary>
/// Tests the diagram size controls on <see cref="UmlRuleDefinition"/>: <c>dedupe</c> (collapse repeated
/// identical interactions) and <c>maxElements</c> (hard cap with a truncation note). These keep a
/// diagram generated from a log that replays the same flow from exceeding the renderer's size limit.
/// </summary>
[TestClass]
public class UmlDedupeAndCapTests
{
    private static UmlRuleDefinition OneMessageRule(bool dedupe, int maxElements) => new()
    {
        Title = "t",
        Dedupe = dedupe,
        MaxElements = maxElements,
        Participants = new() { new UmlParticipant { Id = "A" }, new UmlParticipant { Id = "B" } },
        Rules = new()
        {
            new UmlRule { Name = "ping", Match = "ping", UseRegex = false,
                Action = new UmlAction { Type = "message", From = "A", To = "B", Text = "ping" } },
        },
    };

    private static List<LogMessage> Pings(int n)
        => Enumerable.Range(0, n).Select(_ => new LogMessage { Content = "ping" }).ToList();

    private static int CountArrows(string diagram)
        => diagram.Split('\n').Count(l => l.Contains("->>") || l.Contains("-->>"));

    [TestMethod]
    public void Dedupe_CollapsesIdenticalMessages_ToOne_WithCount()
    {
        var p = new UmlRuleProcessor(new MermaidSyntaxTranslator());
        p.LoadRules(OneMessageRule(dedupe: true, maxElements: 0));
        var diagram = p.ProcessMessages(Pings(100));
        Assert.AreEqual(1, CountArrows(diagram), "100 identical interactions collapse to one");
        StringAssert.Contains(diagram, "×100", "the collapsed count is preserved as an annotation");
    }

    [TestMethod]
    public void Dedupe_DistinctMessages_KeptSeparately()
    {
        // Different extracted/text content must NOT be merged — only identical interactions collapse.
        var def = OneMessageRule(dedupe: true, maxElements: 0);
        var p = new UmlRuleProcessor(new MermaidSyntaxTranslator());
        p.LoadRules(def);
        var msgs = new List<LogMessage>
        {
            new() { Content = "ping" }, new() { Content = "ping" }, new() { Content = "ping" },
        };
        var diagram = p.ProcessMessages(msgs);
        Assert.AreEqual(1, CountArrows(diagram));
        StringAssert.Contains(diagram, "×3");
    }

    [TestMethod]
    public void WithoutDedupe_AllMessagesEmitted()
    {
        var p = new UmlRuleProcessor(new MermaidSyntaxTranslator());
        p.LoadRules(OneMessageRule(dedupe: false, maxElements: 0));
        var diagram = p.ProcessMessages(Pings(10));
        Assert.AreEqual(10, CountArrows(diagram));
    }

    [TestMethod]
    public void MaxElements_CapsOutput_AndAddsTruncationNote()
    {
        var p = new UmlRuleProcessor(new MermaidSyntaxTranslator());
        p.LoadRules(OneMessageRule(dedupe: false, maxElements: 5));
        var diagram = p.ProcessMessages(Pings(100));
        Assert.AreEqual(5, CountArrows(diagram), "emission stops at the cap");
        StringAssert.Contains(diagram, "truncated");
    }

    [TestMethod]
    public void Dedupe_StillCountsAllMatches_ForUsageReport()
    {
        var p = new UmlRuleProcessor(new MermaidSyntaxTranslator());
        p.LoadRules(OneMessageRule(dedupe: true, maxElements: 0));
        p.ProcessMessages(Pings(42));
        Assert.AreEqual(42, p.LastUsage[0].Count, "usage count reflects matches, even though emission deduped");
    }
}
