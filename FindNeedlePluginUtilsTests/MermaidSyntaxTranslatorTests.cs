using FindNeedleUmlDsl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedlePluginUtilsTests;

[TestClass]
public class MermaidSyntaxTranslatorTests
{
    private MermaidSyntaxTranslator _translator = null!;

    [TestInitialize]
    public void Setup()
    {
        _translator = new MermaidSyntaxTranslator();
    }

    [TestMethod]
    public void SyntaxName_ReturnsMermaid()
    {
        Assert.AreEqual("Mermaid", _translator.SyntaxName);
    }

    [TestMethod]
    public void FileExtension_ReturnsMmd()
    {
        Assert.AreEqual(".mmd", _translator.FileExtension);
    }

    [TestMethod]
    public void GenerateHeader_WithTitle_IncludesTitle()
    {
        var definition = new UmlRuleDefinition { Title = "Test Diagram" };

        var header = _translator.GenerateHeader(definition);

        Assert.IsTrue(header.Contains("sequenceDiagram"));
        Assert.IsTrue(header.Contains("title Test Diagram"));
    }

    [TestMethod]
    public void GenerateHeader_WithoutTitle_OmitsTitle()
    {
        var definition = new UmlRuleDefinition();

        var header = _translator.GenerateHeader(definition);

        Assert.IsTrue(header.Contains("sequenceDiagram"));
        Assert.IsFalse(header.Contains("title"));
    }

    [TestMethod]
    public void GenerateFooter_ReturnsEmpty()
    {
        var footer = _translator.GenerateFooter();

        Assert.AreEqual(string.Empty, footer);
    }

    [TestMethod]
    public void GenerateParticipants_WithDisplayName_UsesAsKeyword()
    {
        var participants = new List<UmlParticipant>
        {
            new() { Id = "LSM", DisplayName = "Local Session Manager", Type = "participant" }
        };

        var output = _translator.GenerateParticipants(participants);

        Assert.IsTrue(output.Contains("participant LSM as Local Session Manager"));
    }

    [TestMethod]
    public void GenerateParticipants_WithoutDisplayName_UsesIdOnly()
    {
        var participants = new List<UmlParticipant>
        {
            new() { Id = "LSM", Type = "participant" }
        };

        var output = _translator.GenerateParticipants(participants);

        Assert.IsTrue(output.Contains("participant LSM"));
    }

    [TestMethod]
    public void GenerateParticipants_WithActorType_UsesActorKeyword()
    {
        var participants = new List<UmlParticipant>
        {
            new() { Id = "User", Type = "actor" }
        };

        var output = _translator.GenerateParticipants(participants);

        Assert.IsTrue(output.Contains("actor User"));
    }

    [TestMethod]
    public void GenerateElement_Message_SolidArrow()
    {
        var element = new ResolvedUmlElement
        {
            Type = "message",
            From = "A",
            To = "B",
            Text = "Hello",
            ArrowStyle = "solid"
        };

        var output = _translator.GenerateElement(element);

        Assert.IsTrue(output.Contains("A->>B: Hello"));
    }

    [TestMethod]
    public void GenerateElement_Message_DashedArrow()
    {
        var element = new ResolvedUmlElement
        {
            Type = "message",
            From = "A",
            To = "B",
            Text = "Response",
            ArrowStyle = "dashed"
        };

        var output = _translator.GenerateElement(element);

        Assert.IsTrue(output.Contains("A-->>B: Response"));
    }

    [TestMethod]
    public void GenerateElement_Activate()
    {
        var element = new ResolvedUmlElement
        {
            Type = "activate",
            From = "A"
        };

        var output = _translator.GenerateElement(element);

        Assert.IsTrue(output.Contains("activate A"));
    }

    [TestMethod]
    public void GenerateElement_Deactivate()
    {
        var element = new ResolvedUmlElement
        {
            Type = "deactivate",
            From = "A"
        };

        var output = _translator.GenerateElement(element);

        Assert.IsTrue(output.Contains("deactivate A"));
    }

    [TestMethod]
    public void GenerateElement_Note_LeftPosition()
    {
        var element = new ResolvedUmlElement
        {
            Type = "note",
            From = "A",
            Text = "Important note",
            NotePosition = "left"
        };

        var output = _translator.GenerateElement(element);

        Assert.IsTrue(output.Contains("Note left of A: Important note"));
    }

    [TestMethod]
    public void GenerateElement_EscapesSpecialCharacters()
    {
        var element = new ResolvedUmlElement
        {
            Type = "message",
            From = "A",
            To = "B",
            Text = "Value <test> \"quoted\"",
            ArrowStyle = "solid"
        };

        var output = _translator.GenerateElement(element);

        Assert.IsTrue(output.Contains("&lt;test&gt;"));
        Assert.IsTrue(output.Contains("&quot;quoted&quot;"));
    }
}
