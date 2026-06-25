using FindNeedleUX.Pages.NativeResultViewer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="SourceTypeClassifier"/>, which buckets a row's Source path into the coarse
/// "source type" labels shown as on/off toggles in the Sources dialog.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class SourceTypeClassifierTests
{
    [DataTestMethod]
    [DataRow(@"C:\caps\trace.pcap", "PCAP captures")]
    [DataRow(@"C:\caps\trace.pcapng", "PCAP captures")]
    [DataRow(@"C:\caps\trace.cap", "PCAP captures")]
    [DataRow(@"C:\logs\boot.etl", "ETW (.etl)")]
    [DataRow(@"C:\logs\System.evtx", "Event Log")]
    [DataRow(@"C:\data\rows.csv", "CSV")]
    [DataRow(@"C:\data\rows.tsv", "CSV")]
    [DataRow(@"C:\data\events.json", "JSON")]
    [DataRow(@"C:\bundle\logs.zip", "Zip archives")]
    [DataRow(@"C:\logs\setupact.log", "Log files")]
    [DataRow(@"C:\logs\notes.txt", "Log files")]
    public void Classify_MapsKnownExtensions(string path, string expected)
        => Assert.AreEqual(expected, SourceTypeClassifier.Classify(path));

    [TestMethod]
    public void Classify_UnknownExtension_GoesToOtherWithExt()
        => Assert.AreEqual("Other (.bin)", SourceTypeClassifier.Classify(@"C:\x\data.bin"));

    [TestMethod]
    public void Classify_NoExtension_IsLogFiles()
        => Assert.AreEqual("Log files", SourceTypeClassifier.Classify(@"C:\logs\messages"));

    [TestMethod]
    public void Classify_EmptyOrNull_IsOther()
    {
        Assert.AreEqual("Other", SourceTypeClassifier.Classify(null));
        Assert.AreEqual("Other", SourceTypeClassifier.Classify(""));
        Assert.AreEqual("Other", SourceTypeClassifier.Classify("   "));
    }

    [TestMethod]
    public void Classify_IsCaseInsensitive()
    {
        Assert.AreEqual("PCAP captures", SourceTypeClassifier.Classify(@"C:\X\TRACE.PCAP"));
        Assert.AreEqual("Event Log", SourceTypeClassifier.Classify(@"C:\X\SYSTEM.EVTX"));
    }
}
