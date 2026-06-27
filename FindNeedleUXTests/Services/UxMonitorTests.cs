using System.Linq;
using System.Threading;
using FindNeedleUX.Services.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Services;

/// <summary>Core of the built-in UX latency recorder (Phase 1/2): a Track scope over the threshold is
/// recorded with its nested scope chain; under-threshold work is not. (The input→idle path needs a UI
/// dispatcher, so it's exercised by the app / FlaUI, not here.)</summary>
[TestClass]
public class UxMonitorTests
{
    [TestMethod]
    public void Track_OverThreshold_RecordsWithNestedScopeChain()
    {
        var prev = UxMonitor.ThresholdMs;
        UxMonitor.ThresholdMs = 20;
        try
        {
            using (UxMonitor.Track("Outer"))
            using (UxMonitor.Track("Inner"))
                Thread.Sleep(60);

            var rec = UxMonitor.Recent.LastOrDefault(r => r.Kind == "scope" && r.Interaction == "Inner");
            Assert.IsNotNull(rec, "an over-threshold Track scope should be recorded");
            Assert.IsTrue(rec.LatencyMs >= 20, $"latency should reflect the elapsed time (got {rec.LatencyMs})");
            CollectionAssert.AreEqual(new[] { "Outer", "Inner" }, rec.ScopeChain.ToArray(),
                "the record should carry the nested scope chain (outermost first)");
            Assert.IsFalse(string.IsNullOrEmpty(rec.CallSite), "scopes should capture a call site");
        }
        finally { UxMonitor.ThresholdMs = prev; }
    }

    [TestMethod]
    public void Track_UnderThreshold_NotRecorded()
    {
        var prev = UxMonitor.ThresholdMs;
        UxMonitor.ThresholdMs = 100_000;
        try
        {
            int before = UxMonitor.Recent.Count(r => r.Interaction == "FastUnique_Op");
            using (UxMonitor.Track("FastUnique_Op")) { /* returns immediately */ }
            Assert.AreEqual(before, UxMonitor.Recent.Count(r => r.Interaction == "FastUnique_Op"),
                "work under the threshold must not be recorded");
        }
        finally { UxMonitor.ThresholdMs = prev; }
    }
}
