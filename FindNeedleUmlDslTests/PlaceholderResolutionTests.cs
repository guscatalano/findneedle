using System.Collections.Generic;
using System.Linq;
using FindNeedleUmlDsl;
using FindNeedleUmlDsl.MermaidUML;

namespace FindNeedleUmlDslTests;

/// <summary>
/// Tests placeholder resolution in rule action text — notably that MULTIPLE {extract:} placeholders in
/// one template each resolve to their own capture (regression: they all took the first one's value).
/// </summary>
[TestClass]
public class PlaceholderResolutionTests
{
    private static string Render(string text, string content)
    {
        var def = new UmlRuleDefinition
        {
            Participants = new() { new UmlParticipant { Id = "A" } },
            Rules = new()
            {
                new UmlRule { Name = "r", Match = "X", UseRegex = false,
                    Action = new UmlAction { Type = "note", From = "A", Text = text, NotePosition = "over" } },
            },
        };
        var p = new UmlRuleProcessor(new MermaidSyntaxTranslator());
        p.LoadRules(def);
        return p.ProcessMessages(new List<LogMessage> { new() { Content = content } });
    }

    [TestMethod]
    public void TwoDistinctExtracts_ResolveIndependently()
    {
        var diagram = Render(
            "OS {extract:OS Version=([0-9.]+)} ({extract:architecture=([a-z0-9]+)}) X",
            "Host machine information: OS Version=10.0.26200, Running architecture=amd64 X");
        StringAssert.Contains(diagram, "OS 10.0.26200 (amd64)");
    }

    [TestMethod]
    public void Extract_NoMatch_DropsPlaceholder()
    {
        var diagram = Render("val={extract:zzz=([0-9]+)} X", "nothing here X");
        StringAssert.Contains(diagram, "val=");
        Assert.IsFalse(diagram.Contains("{extract"));
    }

    [TestMethod]
    public void Extract_InvalidRegex_DoesNotThrow_DropsPlaceholder()
    {
        var diagram = Render("v={extract:([unclosed} X", "X");
        Assert.IsFalse(diagram.Contains("{extract"));
    }
}
