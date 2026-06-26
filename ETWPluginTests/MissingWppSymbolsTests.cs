using System;
using System.IO;
using System.Linq;
using findneedle.Implementations.FileExtensions;
using FindNeedlePluginLib;

namespace ETWPluginTests;

/// <summary>
/// Tests the "missing WPP symbols" handling in ETLProcessor — the path the user hit when a WPP ETL's
/// TMFs aren't available (the loader can't format the events). It's driven here through the .txt/.log
/// passthrough using tracefmt's exact "Unknown( N): GUID=… (No Format Information found)." output, so
/// it's deterministic and needs no admin / real ETL.
///
/// NOTE on why this doesn't use a real WppEmitter capture: WppEmitter's WPP traces are *self-describing*
/// — modern ETW embeds the trace-message format (TMF) into the .etl itself — so tracefmt decodes them
/// with zero unknowns even with no symbol path and the binary deleted. That makes WppEmitter unusable
/// for reproducing the missing-symbols case; crafting the unformattable lines directly is the reliable
/// way to exercise the GUID collection, the "Decode anyway" representative rows, and the all-unknown
/// rejection. (See [[wpp-fixture-capture-gotchas]].)
/// </summary>
[TestClass]
public sealed class MissingWppSymbolsTests
{
    private const string GuidA = "11112222-3333-4444-5555-666677778888";
    private const string GuidB = "99998888-7777-6666-5555-444433332222";

    private string _work = null!;

    [TestInitialize]
    public void Setup()
    {
        _work = Path.Combine(Path.GetTempPath(), $"FN_missym_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_work);
        FindNeedlePluginLib.DecodeOptions.ForceFullDecode = false; // default; some tests opt in
    }

    [TestCleanup]
    public void Cleanup()
    {
        FindNeedlePluginLib.DecodeOptions.ForceFullDecode = false;
        try { if (Directory.Exists(_work)) Directory.Delete(_work, true); } catch { }
    }

    // A formatted-output file: one decodable line + several unformattable tracefmt "Unknown(...)"
    // lines (the literal text tracefmt emits when a message GUID has no TMF), across two distinct GUIDs.
    private string WriteMixedFormattedLog(int unknownPerGuid)
    {
        var path = Path.Combine(_work, "wpp.log");
        using var w = new StreamWriter(path);
        w.WriteLine("[0]0ABC.0DEF::06/21/2026-12:00:00.000 [RealProvider]{}");
        for (int i = 0; i < unknownPerGuid; i++)
        {
            w.WriteLine($"Unknown( {i}): GUID={GuidA} (No Format Information found).");
            w.WriteLine($"Unknown( {i}): GUID={GuidB} (No Format Information found).");
        }
        return path;
    }

    [TestMethod]
    public void MissingSymbols_CollectsDistinctMissingTmfGuids_AndSkipsThem()
    {
        var log = WriteMixedFormattedLog(unknownPerGuid: 25);
        using var p = new ETLProcessor();
        p.OpenFile(log);
        p.DoPreProcessing();
        var results = p.GetResults();

        // Only the one decodable line survives; the unformattable events are skipped (not surfaced as rows).
        Assert.AreEqual(1, results.Count, "only the single formattable line should become a row by default");

        // …but the distinct missing message GUIDs are recorded so the resolution log can name the TMFs.
        var info = p.GetDecodeInfo();
        Assert.IsTrue(info.TryGetValue("missingTmfs", out var missing), "missing TMF GUIDs should be reported");
        StringAssert.Contains(missing, GuidA, "GUID A should be listed as a missing TMF");
        StringAssert.Contains(missing, GuidB, "GUID B should be listed as a missing TMF");
    }

    [TestMethod]
    public void DecodeAnyway_EmitsOneRepresentativeRowPerMissingGuid()
    {
        FindNeedlePluginLib.DecodeOptions.ForceFullDecode = true;

        var log = WriteMixedFormattedLog(unknownPerGuid: 25);
        using var p = new ETLProcessor();
        p.OpenFile(log);
        p.DoPreProcessing();
        var results = p.GetResults();

        // "Decode anyway" de-dupes by GUID: the 50 unformattable events collapse to one representative
        // row per distinct GUID (2), alongside the one real row → 3 total.
        Assert.AreEqual(3, results.Count, "1 real row + 1 representative row per distinct missing GUID");
        int unformatted = results.OfType<ETLLogLine>()
            .Count(r => r.tasktxt == "Unformatted (missing WPP symbol)");
        Assert.AreEqual(2, unformatted, "one representative 'unformatted' row per distinct missing GUID");
        Assert.IsTrue(p.GetProviderCount().ContainsKey("(unformatted WPP)"),
            "the collapsed unformattable events should be counted under '(unformatted WPP)'");
    }

    [TestMethod]
    public void AllUnknownLog_IsRejectedByCheckFileFormat()
    {
        // A file with *nothing* decodable (every line is an Unknown(...) line) isn't a valid formatted
        // log — CheckFileFormat must reject it so the folder scan doesn't treat it as parseable.
        var path = Path.Combine(_work, "allunknown.log");
        using (var w = new StreamWriter(path))
            for (int i = 0; i < 50; i++)
                w.WriteLine($"Unknown( {i}): GUID={GuidA} (No Format Information found).");

        using var p = new ETLProcessor();
        p.OpenFile(path);
        Assert.IsFalse(p.CheckFileFormat(), "an all-unknown formatted log should be rejected");
    }
}
