using System;
using System.IO;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Services;

/// <summary>The Home page's row text: relative times, compact row counts, and the source type tag.</summary>
[TestClass]
[TestCategory("ViewModel")]
public class HomeFormattingTests
{
    private static readonly DateTime Now = new(2026, 9, 9, 14, 30, 0);

    [TestMethod]
    public void RelativeTime_ReadsLikeTheMockup()
    {
        Assert.AreEqual("just now", HomeFormatting.RelativeTime(Now.AddSeconds(-20), Now));
        Assert.AreEqual("1 minute ago", HomeFormatting.RelativeTime(Now.AddMinutes(-1), Now));
        Assert.AreEqual("2 minutes ago", HomeFormatting.RelativeTime(Now.AddMinutes(-2), Now));
        Assert.AreEqual("3 hours ago", HomeFormatting.RelativeTime(Now.AddHours(-3), Now));
        Assert.AreEqual("Yesterday", HomeFormatting.RelativeTime(Now.AddDays(-1), Now));
        Assert.AreEqual("Aug 1", HomeFormatting.RelativeTime(new DateTime(2026, 8, 1, 9, 0, 0), Now));
        Assert.AreEqual("Aug 1, 2024", HomeFormatting.RelativeTime(new DateTime(2024, 8, 1), Now));
        Assert.AreEqual("just now", HomeFormatting.RelativeTime(Now.AddMinutes(5), Now), "a future time (clock skew) is 'just now', not negative");
    }

    [TestMethod]
    public void CompactCount_RowsLikeTheMockup()
    {
        Assert.AreEqual("0", HomeFormatting.CompactCount(0));
        Assert.AreEqual("912", HomeFormatting.CompactCount(912));
        Assert.AreEqual("1.5k", HomeFormatting.CompactCount(1500));
        Assert.AreEqual("38k", HomeFormatting.CompactCount(38_000));
        Assert.AreEqual("210k", HomeFormatting.CompactCount(210_400));
        Assert.AreEqual("1.2M", HomeFormatting.CompactCount(1_200_000));
        Assert.AreEqual("36M", HomeFormatting.CompactCount(36_000_000));
        Assert.AreEqual("0", HomeFormatting.CompactCount(-5));
    }

    [TestMethod]
    public void SourceTagForPath_ByExtension()
    {
        Assert.AreEqual("ETW", HomeFormatting.SourceTagForPath(@"C:\traces\kernel-trace-0412.etl"));
        Assert.AreEqual("EventLog", HomeFormatting.SourceTagForPath(@"C:\x\System.EVTX"));
        Assert.AreEqual("Archive", HomeFormatting.SourceTagForPath(@"C:\x\bundle.zip"));
        Assert.AreEqual("Archive", HomeFormatting.SourceTagForPath(@"C:\x\bundle.cab"));
        Assert.AreEqual("Dump", HomeFormatting.SourceTagForPath(@"C:\x\MEMORY.dmp"));
        Assert.AreEqual("CSV", HomeFormatting.SourceTagForPath(@"C:\x\rows.csv"));
        Assert.AreEqual("Pcap", HomeFormatting.SourceTagForPath(@"C:\x\cap.pcapng"));
        Assert.AreEqual("Log", HomeFormatting.SourceTagForPath(@"C:\x\app.log"));
        Assert.AreEqual("Log", HomeFormatting.SourceTagForPath(@"C:\x\notes.txt"));
        Assert.AreEqual("Folder", HomeFormatting.SourceTagForPath(@"\\build-share\logs\svc-host\"));
        Assert.AreEqual("Source", HomeFormatting.SourceTagForPath(""));
    }

    [TestMethod]
    public void SourceTagForPath_ARealDirectoryIsAFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "findneedle-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { Assert.AreEqual("Folder", HomeFormatting.SourceTagForPath(dir)); }
        finally { Directory.Delete(dir); }
    }

    [TestMethod]
    public void SourceTagFor_ByLocationKind()
    {
        Assert.AreEqual("ETW", HomeFormatting.SourceTagFor("FolderLocation", @"C:\t\a.etl"));
        Assert.AreEqual("Kusto", HomeFormatting.SourceTagFor("KustoLocation", "cluster/db"));
        Assert.AreEqual("ADO", HomeFormatting.SourceTagFor("AdoLocation", "12345"));
        Assert.AreEqual("GitHub", HomeFormatting.SourceTagFor("GithubIssuesLocation", "owner/repo#1"));
        Assert.AreEqual("EventLog", HomeFormatting.SourceTagFor("LocalEventLogQueryLocation", "System"));
        Assert.AreEqual("Pcap", HomeFormatting.SourceTagFor("PcapLocation", "x"));
        Assert.AreEqual("Log", HomeFormatting.SourceTagFor(null, @"C:\x\a.log"));
    }
}
