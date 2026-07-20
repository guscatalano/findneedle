using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FindNeedleUX.Services;
using FindNeedleUX.Services.WppSymbols;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.WppSymbols;

/// <summary>
/// CI-runnable tests for <see cref="WppSymbolResolver.BuildTmfs"/>'s DECISIONS — which PDBs get
/// extracted, which get skipped, and in what mode — using the injectable tracepdb seam so no WDK
/// is needed (the real-extraction path is covered by BuildTmfsIntegrationTests in the manual lane).
/// These lock in the issue #4 acceptance criteria: a stale loose PDB is never extracted, resolution
/// happens in managed code, and tracepdb runs only in -f (extract) mode.
/// </summary>
[TestClass]
[TestCategory("WppSymbols")]
[DoNotParallelize] // mutates WppSymbolResolver's static tracepdb overrides
public class BuildTmfsOrchestrationTests
{
    private string _root;
    private readonly List<string> _tracepdbCalls = new();

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"buildtmfs_orch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        WppSymbolResolver.FindTracePdbOverride = () => @"X:\fake\tracepdb.exe";
        WppSymbolResolver.RunTracePdbOverride = (exe, args, log) => _tracepdbCalls.Add(args);
    }

    [TestCleanup]
    public void Cleanup()
    {
        WppSymbolResolver.ResetOverridesForTests();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string NewDir(string name)
    {
        var d = Path.Combine(_root, name);
        Directory.CreateDirectory(d);
        return d;
    }

    [TestMethod]
    public void StaleLoosePdb_IsSkipped_StoreResolvedCopyIsExtracted()
    {
        // A real binary (ntdll) + a STALE loose ntdll.pdb next to it + the MATCHING pdb in a store.
        var work = NewDir("bin");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ntdll.dll"), Path.Combine(work, "ntdll.dll"));
        var id = PdbIdentity.TryReadFromBinary(Path.Combine(work, "ntdll.dll"), out _);
        Assert.IsNotNull(id);

        var stale = Path.Combine(work, id.PdbFileName);
        TestPdbFactory.WriteMsfPdb(stale, Guid.NewGuid(), id.Age); // wrong GUID → stale

        var store = NewDir("store");
        var good = Path.Combine(store, id.PdbFileName, id.Key, id.PdbFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(good));
        TestPdbFactory.WriteMsfPdb(good, id.Guid, id.Age);

        var (_, log) = WppSymbolResolver.BuildTmfs(work, store);

        Assert.AreEqual(1, _tracepdbCalls.Count, $"exactly one extraction expected. Calls:\n{string.Join("\n", _tracepdbCalls)}\nLog:\n{log}");
        StringAssert.Contains(_tracepdbCalls[0], good, "the store-resolved PDB is what gets extracted");
        Assert.IsFalse(_tracepdbCalls.Any(c => c.Contains(stale, StringComparison.OrdinalIgnoreCase)),
            "the stale loose PDB must NEVER reach tracepdb");
        StringAssert.Contains(log, "STALE", "rejection must be loud in the log");
        StringAssert.Contains(log, "skip (stale", "the loose sweep must skip the rejected file");
    }

    [TestMethod]
    public void LoosePdbsWithNoBinary_AreExtractedIndividually()
    {
        // The "folder of PDBs" workflow: no binaries → nothing to verify against → extract each.
        var work = NewDir("pdbs");
        TestPdbFactory.WriteMsfPdb(Path.Combine(work, "alpha.pdb"), Guid.NewGuid(), 1);
        TestPdbFactory.WriteMsfPdb(Path.Combine(work, "beta.pdb"), Guid.NewGuid(), 1);

        WppSymbolResolver.BuildTmfs(work, symbolPath: null);

        Assert.AreEqual(2, _tracepdbCalls.Count);
        Assert.IsTrue(_tracepdbCalls.Any(c => c.Contains("alpha.pdb")));
        Assert.IsTrue(_tracepdbCalls.Any(c => c.Contains("beta.pdb")));
    }

    [TestMethod]
    public void SameResolvedPdb_ForTwoBinaries_ExtractedOnce()
    {
        // Two copies of the same binary share one PDB identity — extraction must dedupe.
        var work = NewDir("dup");
        var ntdll = Path.Combine(Environment.SystemDirectory, "ntdll.dll");
        File.Copy(ntdll, Path.Combine(work, "ntdll.dll"));
        File.Copy(ntdll, Path.Combine(work, "ntdll_copy.dll"));
        var id = PdbIdentity.TryReadFromBinary(ntdll, out _);
        TestPdbFactory.WriteMsfPdb(Path.Combine(work, id.PdbFileName), id.Guid, id.Age); // matching loose

        var (_, log) = WppSymbolResolver.BuildTmfs(work, symbolPath: null);

        Assert.AreEqual(1, _tracepdbCalls.Count,
            $"one shared PDB → one extraction. Calls:\n{string.Join("\n", _tracepdbCalls)}\nLog:\n{log}");
    }

    [TestMethod]
    public void ResolvedPdbYieldingNoTmfs_ExplainsStrippedPublicSymbols()
    {
        // Research-verified gotcha (issue #4 follow-up): WPP trace-format data is stripped from
        // public symbols, and a stripped PDB keeps the private PDB's GUID+age — so "resolved fine,
        // zero TMFs" must self-explain. The fake runner extracts nothing, simulating exactly that.
        var work = NewDir("stripped");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ntdll.dll"), Path.Combine(work, "ntdll.dll"));
        var id = PdbIdentity.TryReadFromBinary(Path.Combine(work, "ntdll.dll"), out _);
        TestPdbFactory.WriteMsfPdb(Path.Combine(work, id.PdbFileName), id.Guid, id.Age);

        var (_, log) = WppSymbolResolver.BuildTmfs(work, symbolPath: null);

        StringAssert.Contains(log, "produced no TMFs", $"zero-TMF note missing. Log:\n{log}");
        StringAssert.Contains(log, "public/stripped", "the note must name the likely cause");
    }

    [TestMethod]
    public void EveryTracepdbCall_IsExtractOnly_NeverResolveMode()
    {
        var work = NewDir("mode");
        File.Copy(Path.Combine(Environment.SystemDirectory, "ntdll.dll"), Path.Combine(work, "ntdll.dll"));
        var id = PdbIdentity.TryReadFromBinary(Path.Combine(work, "ntdll.dll"), out _);
        TestPdbFactory.WriteMsfPdb(Path.Combine(work, id.PdbFileName), id.Guid, id.Age);
        TestPdbFactory.WriteMsfPdb(Path.Combine(work, "extra.pdb"), Guid.NewGuid(), 3);

        WppSymbolResolver.BuildTmfs(work, symbolPath: @"srv*C:\nonexistent-store");

        Assert.IsTrue(_tracepdbCalls.Count >= 2, "both the resolved and the loose pdb extract");
        foreach (var call in _tracepdbCalls)
        {
            StringAssert.Contains(call, "-f ", $"extract-only mode expected: {call}");
            Assert.IsFalse(call.Contains("-i "), $"the opaque tracepdb -i mode must never be used: {call}");
        }
    }
}
