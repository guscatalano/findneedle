using FindNeedlePluginUtils.UmlDsl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedlePluginUtilsTests;

[TestClass]
public class PlantUmlSyntaxTranslatorTests
{
    private PlantUmlSyntaxTranslator _translator = null!;

    [TestInitialize]
    public void Setup()
    {
        _translator = new PlantUmlSyntaxTranslator();
    }

    [TestMethod]
    public void SyntaxName_ReturnsPlantUML()
    {
        Assert.AreEqual("PlantUML", _translator.SyntaxName);
    }

    [TestMethod]
    public void FileExtension_ReturnsPu()
    {
        Assert.AreEqual(".pu", _translator.FileExtension);
    }

    [TestMethod]
    public void GenerateHeader_WithTitle_IncludesTitle()
    {
        var definition = new UmlRuleDefinition { Title = "Test Diagram" };

        var header = _translator.GenerateHeader(definition);

        Assert.IsTrue(header.Contains("@startuml"));
        Assert.IsTrue(header.Contains("title Test Diagram"));
    }

    [TestMethod]
    public void GenerateHeader_WithoutTitle_OmitsTitle()
    {
        var definition = new UmlRuleDefinition();

        var header = _translator.GenerateHeader(definition);

        Assert.IsTrue(header.Contains("@startuml"));
        Assert.IsFalse(header.Contains("title"));
    }

    [TestMethod]
    public void GenerateFooter_ReturnsEnduml()
    {
        var footer = _translator.GenerateFooter();

        Assert.AreEqual("@enduml", footer);
    }

    [TestMethod]
    public void GenerateParticipants_WithDisplayName_UsesAlias()
    {
        var participants = new List<UmlParticipant>
        {
            new() { Id = "LSM", DisplayName = "Local Session Manager", Type = "participant" }
        };

        var output = _translator.GenerateParticipants(participants);

        Assert.IsTrue(output.Contains("participant \"Local Session Manager\" as LSM"));
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

        Assert.AreEqual("A -> B : Hello", output);
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

        Assert.AreEqual("A --> B : Response", output);
    }

    [TestMethod]
    public void GenerateElement_Message_AsyncArrow()
    {
        var element = new ResolvedUmlElement
        {
            Type = "message",
            From = "A",
            To = "B",
            Text = "Async call",
            ArrowStyle = "async"
        };

        var output = _translator.GenerateElement(element);

        Assert.AreEqual("A ->> B : Async call", output);
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

        Assert.AreEqual("activate A", output);
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

        Assert.AreEqual("deactivate A", output);
    }

    [TestMethod]
    public void GenerateElement_Divider()
    {
        var element = new ResolvedUmlElement
        {
            Type = "divider",
            Text = "Section Break"
        };

        var output = _translator.GenerateElement(element);

        Assert.AreEqual("== Section Break ==", output);
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

        Assert.AreEqual("note left of A : Important note", output);
    }

    [TestMethod]
    public void GenerateElement_Note_RightPosition()
    {
        var element = new ResolvedUmlElement
        {
            Type = "note",
            From = "A",
            Text = "Important note",
            NotePosition = "right"
        };

        var output = _translator.GenerateElement(element);

        Assert.AreEqual("note right of A : Important note", output);
    }
}
