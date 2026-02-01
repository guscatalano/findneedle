using FindNeedlePluginLib;
using FindNeedleUmlDsl;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedlePluginUtilsTests;

[TestClass]
public class UmlGeneratorTests
{
    [TestMethod]
    public void PlantUMLGenerator_ImplementsIUMLGenerator()
    {
        var generator = new PlantUMLGenerator();

        Assert.IsInstanceOfType(generator, typeof(IUMLGenerator));
    }

    [TestMethod]
    public void PlantUMLGenerator_Name_ReturnsPlantUML()
    {
        var generator = new PlantUMLGenerator();

        Assert.AreEqual("PlantUML", generator.Name);
    }

    [TestMethod]
    public void PlantUMLGenerator_InputFileExtension_ReturnsPu()
    {
        var generator = new PlantUMLGenerator();

        Assert.AreEqual(".pu", generator.InputFileExtension);
    }

    [TestMethod]
    public void MermaidUMLGenerator_ImplementsIUMLGenerator()
    {
        var generator = new MermaidUMLGenerator();

        Assert.IsInstanceOfType(generator, typeof(IUMLGenerator));
    }

    [TestMethod]
    public void MermaidUMLGenerator_Name_ReturnsMermaid()
    {
        var generator = new MermaidUMLGenerator();

        Assert.AreEqual("Mermaid", generator.Name);
    }

    [TestMethod]
    public void MermaidUMLGenerator_InputFileExtension_ReturnsMmd()
    {
        var generator = new MermaidUMLGenerator();

        Assert.AreEqual(".mmd", generator.InputFileExtension);
    }

    [TestMethod]
    public void PlantUMLGenerator_BrowserOutput_AlwaysSupported()
    {
        var generator = new PlantUMLGenerator();

        Assert.IsTrue(generator.IsSupported(UmlOutputType.Browser));
    }

    [TestMethod]
    public void MermaidUMLGenerator_BrowserOutput_AlwaysSupported()
    {
        var generator = new MermaidUMLGenerator();

        Assert.IsTrue(generator.IsSupported(UmlOutputType.Browser));
    }

    [TestMethod]
    public void PlantUMLGenerator_SupportedOutputTypes_IncludesBrowser()
    {
        var generator = new PlantUMLGenerator();

        CollectionAssert.Contains(generator.SupportedOutputTypes, UmlOutputType.Browser);
    }

    [TestMethod]
    public void MermaidUMLGenerator_SupportedOutputTypes_IncludesBrowser()
    {
        var generator = new MermaidUMLGenerator();

        CollectionAssert.Contains(generator.SupportedOutputTypes, UmlOutputType.Browser);
    }

    [TestMethod]
    public void PlantUMLGenerator_GenerateUML_ThrowsOnInvalidPath()
    {
        var generator = new PlantUMLGenerator();

        Assert.ThrowsException<Exception>(() => generator.GenerateUML("nonexistent.pu", UmlOutputType.Browser));
    }

    [TestMethod]
    public void MermaidUMLGenerator_GenerateUML_ThrowsOnInvalidPath()
    {
        var generator = new MermaidUMLGenerator();

        Assert.ThrowsException<Exception>(() => generator.GenerateUML("nonexistent.mmd", UmlOutputType.Browser));
    }

    [TestMethod]
    public void MermaidUMLGenerator_GenerateBrowserHtml_CreatesHtmlFile()
    {
        var generator = new MermaidUMLGenerator();
        var tempFile = Path.GetTempFileName();
        var mmdFile = Path.ChangeExtension(tempFile, ".mmd");
        
        try
        {
            File.WriteAllText(mmdFile, "sequenceDiagram\n    A->>B: Hello");
            
            var result = generator.GenerateUML(mmdFile, UmlOutputType.Browser);
            
            Assert.IsTrue(result.EndsWith(".html"));
            Assert.IsTrue(File.Exists(result));
            
            var content = File.ReadAllText(result);
            Assert.IsTrue(content.Contains("mermaid"));
            Assert.IsTrue(content.Contains("sequenceDiagram"));
        }
        finally
        {
            if (File.Exists(mmdFile)) File.Delete(mmdFile);
            var htmlFile = Path.ChangeExtension(mmdFile, ".html");
            if (File.Exists(htmlFile)) File.Delete(htmlFile);
        }
    }

    [TestMethod]
    public void PlantUMLGenerator_GenerateBrowserHtml_CreatesHtmlFile()
    {
        var generator = new PlantUMLGenerator();
        var tempFile = Path.GetTempFileName();
        var puFile = Path.ChangeExtension(tempFile, ".pu");
        
        try
        {
            File.WriteAllText(puFile, "@startuml\nA -> B : Hello\n@enduml");
            
            var result = generator.GenerateUML(puFile, UmlOutputType.Browser);
            
            Assert.IsTrue(result.EndsWith(".html"));
            Assert.IsTrue(File.Exists(result));
            
            var content = File.ReadAllText(result);
            Assert.IsTrue(content.Contains("plantuml.com"));
            Assert.IsTrue(content.Contains("@startuml"));
        }
        finally
        {
            if (File.Exists(puFile)) File.Delete(puFile);
            var htmlFile = Path.ChangeExtension(puFile, ".html");
            if (File.Exists(htmlFile)) File.Delete(htmlFile);
        }
    }
}
