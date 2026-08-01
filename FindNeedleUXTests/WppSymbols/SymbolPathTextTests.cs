using FindNeedleUX.Services;
using FindPluginCore.Wpp.Symbols;
using FindPluginCore.Wpp.Symbols.WppSymbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.WppSymbols;

/// <summary>
/// Covers the pure text/round-trip logic behind the WPP resolution page's editors (folder lists,
/// multiline symbol path) and the server-awareness the status table uses. No UI, CI-runnable.
/// </summary>
[TestClass]
[TestCategory("WppSymbols")]
public class SymbolPathTextTests
{
    [TestMethod]
    public void Split_TrimsAndDropsEmpties()
    {
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, SymbolPathText.Split("  a ; ; b;;c  "));
        Assert.AreEqual(0, SymbolPathText.Split(null).Count);
        Assert.AreEqual(0, SymbolPathText.Split("   ").Count);
    }

    [TestMethod]
    public void Join_TrimsAndDropsEmpties()
        => Assert.AreEqual("a;b", SymbolPathText.Join(new[] { " a ", "", "  ", "b" }));

    [TestMethod]
    public void ToLines_FromLines_RoundTrip()
    {
        Assert.AreEqual("a\nb\nc", SymbolPathText.ToLines("a;b;c"));
        Assert.AreEqual("a;b;c", SymbolPathText.FromLines("a\nb\r\nc"));
        // A clean setting survives a display→edit→save round-trip unchanged.
        Assert.AreEqual("a;b;c", SymbolPathText.FromLines(SymbolPathText.ToLines("a;b;c")));
    }

    [TestMethod]
    public void FromLines_DropsBlankLines()
        => Assert.AreEqual("a;b", SymbolPathText.FromLines("a\n\n  \nb\n"));

    [TestMethod]
    public void AppendFolder_DedupesCaseInsensitive_AndIgnoresBlank()
    {
        Assert.AreEqual(@"C:\a;C:\b", SymbolPathText.AppendFolder(@"C:\a", @"C:\b"));
        Assert.AreEqual(@"C:\a", SymbolPathText.AppendFolder(@"C:\a", @"c:\A"));   // case-insensitive dup
        Assert.AreEqual(@"C:\a", SymbolPathText.AppendFolder(@"C:\a", "   "));       // blank ignored
        Assert.AreEqual(@"C:\a", SymbolPathText.AppendFolder("", @"C:\a"));          // into empty
    }

    [TestMethod]
    public void HasHttpSymbolServer_DetectsServer()
    {
        Assert.IsTrue(WppSymbolResolver.HasHttpSymbolServer(@"srv*C:\cache*https://msdl.microsoft.com/download/symbols"));
        Assert.IsTrue(WppSymbolResolver.HasHttpSymbolServer(@"srv*http://sym.example/store"));
    }

    [TestMethod]
    public void HasHttpSymbolServer_FalseForLocalOnly()
    {
        Assert.IsFalse(WppSymbolResolver.HasHttpSymbolServer(@"C:\pdbs;D:\more"));
        Assert.IsFalse(WppSymbolResolver.HasHttpSymbolServer(""));
        Assert.IsFalse(WppSymbolResolver.HasHttpSymbolServer(null));
    }
}
