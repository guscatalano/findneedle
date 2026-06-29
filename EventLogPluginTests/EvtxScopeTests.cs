using System;
using System.Collections.Generic;
using System.Linq;
using findneedle.Implementations.FileExtensions;
using FindNeedlePluginLib;

namespace EventLogPluginTests;

/// <summary>
/// The decode-time triage scope (<see cref="DecodeScope"/>) applied to Event Log (.evtx). EVTX has a real
/// provider + timestamp on each record, so the scope filters on both BEFORE the expensive wrap
/// (EventRecordResult's ctor calls FormatDescription() to render the message). Out-of-scope events are never
/// wrapped or loaded. Uses the committed 3-event Sysmon fixture (SampleFiles\susp_explorer_exec.evtx).
/// </summary>
[TestClass]
public sealed class EvtxScopeTests
{
    private const string Sample = "SampleFiles\\susp_explorer_exec.evtx";

    [TestCleanup]
    public void Cleanup() => DecodeScope.Current = null; // don't leak the global scope to other tests

    private static List<ISearchResult> Load(DecodeScope scope)
    {
        DecodeScope.Current = scope; // read by FileEventLogQueryLocation.Search() during GetResults()
        var p = new EVTXProcessor();
        p.OpenFile(Sample);
        p.LoadInMemory();
        return p.GetResults();
    }

    private static HashSet<string> Set(IEnumerable<string> items) => new(items, StringComparer.OrdinalIgnoreCase);

    [TestMethod]
    public void Scope_Provider_FiltersAtDecodeTime()
    {
        var full = Load(null);
        Assert.IsTrue(full.Count > 0, "fixture should load some events");
        var providers = full.Select(r => r.GetSource()).Distinct().ToList();

        // Exclude every provider present -> nothing survives the decode-time filter.
        Assert.AreEqual(0, Load(new DecodeScope { ExcludeProviders = Set(providers) }).Count,
            "excluding all present providers drops every event before the wrap");

        // Allow-list the real providers -> everything kept (no false drops).
        Assert.AreEqual(full.Count, Load(new DecodeScope { IncludeProviders = Set(providers) }).Count,
            "an allow-list of the present providers keeps every event");

        // Allow-list only a provider that isn't present -> nothing kept.
        Assert.AreEqual(0, Load(new DecodeScope { IncludeProviders = Set(new[] { "Nonexistent-Provider" }) }).Count,
            "an allow-list missing the present providers drops everything");
    }

    [TestMethod]
    public void Scope_TimeWindow_FiltersAtDecodeTime()
    {
        var full = Load(null);
        Assert.IsTrue(full.Count > 0, "fixture should load some events");
        var maxUtc = full.Max(r => r.GetLogTime().ToUniversalTime());
        var minUtc = full.Min(r => r.GetLogTime().ToUniversalTime());

        // A window entirely AFTER every event -> nothing kept.
        Assert.AreEqual(0, Load(new DecodeScope { FromUtc = maxUtc.AddDays(1) }).Count,
            "a window after all events drops everything");

        // A window covering all events -> everything kept.
        Assert.AreEqual(full.Count, Load(new DecodeScope { FromUtc = minUtc.AddDays(-1), ToUtc = maxUtc.AddDays(1) }).Count,
            "a window spanning all events keeps everything");
    }
}
