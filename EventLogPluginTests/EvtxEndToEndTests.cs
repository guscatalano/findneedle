using System.Collections.Generic;
using System.IO;
using System.Linq;
using findneedle.Implementations;
using findneedle.Implementations.FileExtensions;
using FindNeedlePluginLib;
using FindPluginCore.PluginSubsystem;
using FindPluginCore.Searching;

namespace EventLogPluginTests;

/// <summary>
/// End-to-end check that a Windows Event Log (.evtx) file loads through the full search pipeline
/// the UX drives — a real .evtx parsed by the real EVTXProcessor, run through
/// NuSearchQuery.RunThrough, with results read back from storage like the viewer does. Mirrors the
/// .log / .etl end-to-end tests. Uses the committed Sysmon sample (deterministic: 3 events).
/// </summary>
[TestClass]
public sealed class EvtxEndToEndTests
{
    [TestMethod]
    public void OpenEvtx_RunSearch_LoadsResults()
    {
        var evtx = Path.GetFullPath(Path.Combine("SampleFiles", "susp_explorer_exec.evtx"));
        Assert.IsTrue(File.Exists(evtx), $"sample .evtx should be deployed: {evtx}");

        var loc = new FolderLocation { path = evtx };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new EVTXProcessor() });

        var query = new NuSearchQuery { OverrideStorageType = StorageType.InMemory };
        query.Locations.Add(loc);
        query.RunThrough();

        var storage = query.ResultStorage;
        Assert.IsNotNull(storage, "search should have created result storage");

        var raw = new List<ISearchResult>();
        storage!.GetRawResultsInBatches(b => raw.AddRange(b), 1000);
        Assert.AreEqual(3, raw.Count, "the sample .evtx has 3 events; all should load through the pipeline");

        // Results reached the filtered/viewable store too.
        var filtered = new List<ISearchResult>();
        storage.GetFilteredResultsInBatches(b => filtered.AddRange(b), 1000);
        Assert.IsTrue(filtered.Count >= 1, "loaded events should reach the viewable (filtered) store");

        // Parsed correctly end-to-end (matches the processor-level sample test).
        Assert.IsTrue(raw.Any(r => r.GetSource() == "Microsoft-Windows-Sysmon"),
            "the Sysmon provider should be present in the loaded results");
    }
}
