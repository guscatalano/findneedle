using System;
using System.IO;
using System.Linq;
using BasicTextPlugin;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// The decode-time triage scope (<see cref="DecodeScope"/>) extended to plain text. Text has no provider or
/// reliable level at decode time, so only the TIME WINDOW applies — and only to lines that actually parse a
/// timestamp; an un-timestamped line (e.g. a wrapped stack frame) is always kept since we can't classify it.
/// Mirrors the ETL/EVTX decode filter: out-of-window rows are never added to the result set.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PlainTextScopeTests
{
    [TestCleanup]
    public void Cleanup() => DecodeScope.Current = null; // don't leak the global scope to other tests

    private static string WriteLog()
    {
        var dir = Path.Combine(Path.GetTempPath(), "FN_txtscope_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "app.log");
        File.WriteAllLines(f, new[]
        {
            "2026-06-01 10:00:00 line A",
            "2026-06-01 11:00:00 line B",
            "2026-06-01 12:00:00 line C",
            "    continued frame with no timestamp",
        });
        return f;
    }

    private static System.Collections.Generic.List<string> Load(string f, DecodeScope scope)
    {
        DecodeScope.Current = scope;
        var proc = new PlainTextProcessor();
        proc.OpenFile(f);
        proc.LoadInMemory();
        return proc.GetResults().Select(r => r.GetMessage()).ToList();
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void NoScope_KeepsEveryLine()
    {
        var f = WriteLog();
        try { Assert.AreEqual(4, Load(f, null).Count); }
        finally { TryDelete(f); }
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void TimeWindowScope_DropsOutOfWindowLines_KeepsUntimestamped()
    {
        var f = WriteLog();
        try
        {
            // From 11:00 (local — text timestamps carry no tz, so the decoder treats them as local): line A
            // at 10:00 is before the window and must be dropped; B/C are in-window; the un-timestamped line
            // can't be classified, so it stays.
            var from = new DateTime(2026, 6, 1, 11, 0, 0, DateTimeKind.Local).ToUniversalTime();
            var texts = Load(f, new DecodeScope { FromUtc = from });

            Assert.AreEqual(3, texts.Count, "line A (10:00) is before the window and must be dropped");
            Assert.IsFalse(texts.Any(t => t.Contains("line A")), "out-of-window line dropped at decode time");
            Assert.IsTrue(texts.Any(t => t.Contains("line B")));
            Assert.IsTrue(texts.Any(t => t.Contains("line C")));
            Assert.IsTrue(texts.Any(t => t.Contains("continued frame")), "un-timestamped line is always kept");
        }
        finally { TryDelete(f); }
    }

    [TestMethod]
    [TestCategory("Storage")]
    public void ProviderOnlyScope_DoesNotDropText()
    {
        // A provider allow-list (e.g. carried over from an ETL triage in a mixed folder) must NOT nuke text:
        // plain text has no provider, so the provider dimension is skipped and every line survives.
        var f = WriteLog();
        try
        {
            var scope = new DecodeScope
            {
                IncludeProviders = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Some-ETW-Provider" }
            };
            Assert.AreEqual(4, Load(f, scope).Count, "provider-only scope leaves text untouched (no provider to match)");
        }
        finally { TryDelete(f); }
    }

    private static void TryDelete(string f)
    {
        DecodeScope.Current = null;
        try { Directory.Delete(Path.GetDirectoryName(f)!, true); } catch { /* best effort */ }
    }
}
