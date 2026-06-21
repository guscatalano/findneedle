using System.IO;
using System.Linq;
using System.Text.Json;

namespace FindNeedleUmlDslTests;

/// <summary>
/// Validates how the DISM rules are registered in CommonRules/common-rules.manifest.json: the emitter
/// auto-adds for *dism* paths, and the UML participants/messages helper is marked Hidden so it doesn't
/// show up as a standalone auto-add rule. Catches accidental de-registration / wrong condition.
/// </summary>
[TestClass]
public class DismRulesManifestTests
{
    private static JsonElement LoadManifest()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var c = Path.Combine(dir.FullName, "FindNeedleUX", "CommonRules", "common-rules.manifest.json");
            if (File.Exists(c)) return JsonDocument.Parse(File.ReadAllText(c)).RootElement;
            dir = dir.Parent;
        }
        Assert.Fail("common-rules.manifest.json not found");
        return default;
    }

    private static JsonElement Entry(string file) =>
        LoadManifest().EnumerateArray().First(e => e.GetProperty("File").GetString() == file);

    private static bool Has(string file) =>
        LoadManifest().EnumerateArray().Any(e => e.GetProperty("File").GetString() == file);

    [TestMethod]
    public void Emitter_IsRegistered_AndAutoAddsForDismPaths()
    {
        Assert.IsTrue(Has("dism-interaction.rules.json"), "emitter must be registered");
        var e = Entry("dism-interaction.rules.json");
        var globs = e.GetProperty("Condition").GetProperty("PathGlobs").EnumerateArray()
            .Select(g => g.GetString()).ToList();
        CollectionAssert.Contains(globs, "*dism*");
    }

    [TestMethod]
    public void UmlHelper_IsRegistered_AsHidden()
    {
        Assert.IsTrue(Has("dism-interaction-uml.rules.json"), "uml helper must be registered");
        var e = Entry("dism-interaction-uml.rules.json");
        Assert.IsTrue(e.TryGetProperty("Hidden", out var h) && h.GetBoolean(),
            "the UML participants/messages file is a helper → Hidden (not a standalone auto-add rule)");
    }

    [TestMethod]
    public void Emitter_PointsAtTheUmlHelper()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        string emitter = null;
        while (dir != null)
        {
            var c = Path.Combine(dir.FullName, "FindNeedleUX", "CommonRules", "dism-interaction.rules.json");
            if (File.Exists(c)) { emitter = c; break; }
            dir = dir.Parent;
        }
        Assert.IsNotNull(emitter);
        var json = File.ReadAllText(emitter);
        StringAssert.Contains(json, "dism-interaction-uml.rules.json");
        StringAssert.Contains(json, "\"type\": \"uml\"");
    }
}
