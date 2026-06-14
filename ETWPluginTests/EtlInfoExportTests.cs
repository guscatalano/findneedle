using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Xml.Linq;
using findneedle.ETWPlugin;

namespace ETWPluginTests;

/// <summary>
/// Tests the "copy as" exporters on EtlInfoExtractor (plaintext / JSON / XML / CSV). Builds an
/// EtlInfo directly so it's fast and deterministic (no ETW needed) — runs in CI.
/// </summary>
[TestClass]
public sealed class EtlInfoExportTests
{
    private static EtlInfo Sample() => new()
    {
        FilePath = @"C:\traces\sample.etl",
        FileSizeBytes = 2_500_000,
        OsVersion = "10.0.26100",
        PointerSizeBits = 64,
        NumberOfProcessors = 16,
        CpuSpeedMHz = 3294,
        SessionStartTime = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        SessionEndTime = new DateTime(2026, 1, 2, 3, 4, 7, DateTimeKind.Utc),
        SessionDuration = TimeSpan.FromSeconds(2),
        EventCount = 1503,
        EventsLost = 1,
        KernelEventCount = 3,
        ManifestOrTraceLoggingEventCount = 1500,
        Providers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft-Windows-Kernel-Process", 1000 },
            { "My-Provider, Inc.", 503 }, // comma + name to exercise CSV quoting
        },
    };

    [TestMethod]
    public void ToJson_IsValidAndHasFields()
    {
        var json = EtlInfoExtractor.ToJson(Sample());
        using var doc = JsonDocument.Parse(json); // throws if invalid
        var root = doc.RootElement;
        Assert.AreEqual("10.0.26100", root.GetProperty("OsVersion").GetString());
        Assert.AreEqual(1503, root.GetProperty("EventCount").GetInt32());
        Assert.IsTrue(root.GetProperty("Providers").TryGetProperty("Microsoft-Windows-Kernel-Process", out var c) && c.GetInt32() == 1000);
    }

    [TestMethod]
    public void ToXml_IsValidAndHasProviders()
    {
        var xml = EtlInfoExtractor.ToXml(Sample());
        var doc = XDocument.Parse(xml); // throws if invalid
        Assert.AreEqual("10.0.26100", (string)doc.Root!.Element("OsVersion")!);
        var providers = doc.Root.Element("Providers")!.Elements("Provider");
        Assert.AreEqual(2, providers.Count());
        Assert.IsTrue(providers.Any(p => (string)p.Attribute("name")! == "My-Provider, Inc." && (int)p.Attribute("count")! == 503));
    }

    [TestMethod]
    public void ToCsv_HasHeadersAndQuotesCommas()
    {
        var csv = EtlInfoExtractor.ToCsv(Sample());
        StringAssert.Contains(csv, "Field,Value");
        StringAssert.Contains(csv, "Provider,Count");
        StringAssert.Contains(csv, "OsVersion,\"10.0.26100\"");
        // The provider name with a comma must be quoted so the CSV stays valid.
        StringAssert.Contains(csv, "\"My-Provider, Inc.\",503");
    }

    [TestMethod]
    public void ToPlainText_MatchesFormat()
    {
        var info = Sample();
        Assert.AreEqual(EtlInfoExtractor.Format(info), EtlInfoExtractor.ToPlainText(info));
        StringAssert.Contains(EtlInfoExtractor.ToPlainText(info), "10.0.26100");
    }
}
