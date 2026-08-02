using System;
using System.Linq;
using FindPluginCore.Wpp.Symbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>The shared WDK trace-tool env setup used by BOTH the GUI and the CLI (they used to diverge —
/// the CLI didn't set these at all, so a trace could decode in the GUI but not the CLI).</summary>
[TestClass]
public sealed class TraceFormatEnvTests
{
    private string _tmf, _sym;

    [TestInitialize]
    public void Save()
    {
        _tmf = Environment.GetEnvironmentVariable(TraceFormatEnv.TmfVar);
        _sym = Environment.GetEnvironmentVariable(TraceFormatEnv.SymVar);
    }

    [TestCleanup]
    public void Restore()
    {
        Environment.SetEnvironmentVariable(TraceFormatEnv.TmfVar, _tmf);
        Environment.SetEnvironmentVariable(TraceFormatEnv.SymVar, _sym);
    }

    [TestMethod]
    public void Apply_PutsConfiguredFolderFirst_AmbientAtTail()
    {
        TraceFormatEnv.Apply(@"C:\my\tmf", @"srv*http://sym", ambientTmf: @"C:\ambient\tmf", ambientSym: @"C:\ambient\sym");

        var tmf = Environment.GetEnvironmentVariable(TraceFormatEnv.TmfVar);
        StringAssert.StartsWith(tmf, @"C:\my\tmf", "configured TMF folder should be searched first");
        StringAssert.Contains(tmf, @"C:\ambient\tmf", "ambient TRACE_FORMAT_SEARCH_PATH must be preserved");
        // (The managed TMF cache is inserted between them only if the cache dir exists — not asserted here.)

        Assert.AreEqual(@"srv*http://sym;C:\ambient\sym",
            Environment.GetEnvironmentVariable(TraceFormatEnv.SymVar), "_NT_SYMBOL_PATH = configured + ambient");
    }

    [TestMethod]
    public void Apply_EmptySymbol_KeepsAmbientSymbolPath()
    {
        TraceFormatEnv.Apply(tmfFolder: null, symbolPath: "  ", ambientTmf: "", ambientSym: @"C:\amb\sym");
        Assert.AreEqual(@"C:\amb\sym", Environment.GetEnvironmentVariable(TraceFormatEnv.SymVar));
    }

    [TestMethod]
    public void Apply_Dedupes_CaseInsensitive()
    {
        // A folder that also appears in ambient shouldn't be listed twice (case-insensitively). The managed
        // TMF cache dir may or may not exist on the machine, so count occurrences of the duplicated path
        // specifically rather than the total entry count.
        TraceFormatEnv.Apply(@"C:\Dup", null, ambientTmf: @"c:\dup", ambientSym: "");
        var tmf = Environment.GetEnvironmentVariable(TraceFormatEnv.TmfVar);
        var dupCount = tmf.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Count(p => p.Trim().Equals(@"C:\Dup", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(1, dupCount, $"the duplicated path should appear once, got: '{tmf}'");
    }
}
