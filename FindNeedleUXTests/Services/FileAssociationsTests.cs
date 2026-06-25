using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Services;

/// <summary>
/// Tests for <see cref="FileAssociations.IsSupported"/> — the single source of truth for which file
/// extensions Find Needle advertises as openable (drag-drop / open-with). Keep in sync with the
/// packaged manifest.
/// </summary>
[TestClass]
[TestCategory("Services")]
public class FileAssociationsTests
{
    [DataTestMethod]
    [DataRow(@"C:\logs\a.etl")]
    [DataRow(@"C:\logs\a.evtx")]
    [DataRow(@"C:\logs\a.log")]
    [DataRow(@"C:\logs\a.txt")]
    [DataRow(@"C:\logs\a.zip")]
    [DataRow(@"C:\logs\a.cab")]
    public void IsSupported_TrueForAdvertisedExtensions(string path)
        => Assert.IsTrue(FileAssociations.IsSupported(path));

    [TestMethod]
    public void IsSupported_IsCaseInsensitive()
    {
        Assert.IsTrue(FileAssociations.IsSupported(@"C:\X\TRACE.ETL"));
        Assert.IsTrue(FileAssociations.IsSupported(@"C:\X\Bundle.CAB"));
    }

    [DataTestMethod]
    [DataRow(@"C:\data\a.csv")]   // CSV is parsed once opened, but not an advertised association target
    [DataRow(@"C:\data\a.pcap")]
    [DataRow(@"C:\data\a.bin")]
    [DataRow(@"C:\data\noext")]
    public void IsSupported_FalseForOtherExtensions(string path)
        => Assert.IsFalse(FileAssociations.IsSupported(path));

    [TestMethod]
    public void IsSupported_FalseForNullOrBlank()
    {
        Assert.IsFalse(FileAssociations.IsSupported(null));
        Assert.IsFalse(FileAssociations.IsSupported(""));
        Assert.IsFalse(FileAssociations.IsSupported("   "));
    }
}
