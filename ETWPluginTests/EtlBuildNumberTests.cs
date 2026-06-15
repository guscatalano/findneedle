using System.IO;
using findneedle.ETWPlugin;

namespace ETWPluginTests;

/// <summary>
/// Verifies EtlInfoExtractor reads the real OS build number from the ETW header event
/// (ProviderVersion) rather than the often-bogus header OSVersion (10.0.0.0). The committed sample
/// was captured on build 26100. (Note: the BuildLabEx branch like "rs_prerelease" is a registry
/// value and is not present in ETW traces, so it can't be extracted.)
/// </summary>
[TestClass]
public sealed class EtlBuildNumberTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(180000)]
    public void Inspect_SampleEtl_ReadsBuildNumberFromHeader()
    {
        var etl = Path.GetFullPath(Path.Combine("SampleFiles", "test.etl"));
        Assert.IsTrue(File.Exists(etl), etl);

        var info = EtlInfoExtractor.Inspect(etl);
        TestContext.WriteLine($"OsVersion={info.OsVersion}  BuildNumber={info.BuildNumber}  BuildLab={info.BuildLab}  Branch={info.Branch}");

        Assert.AreEqual(26100, info.BuildNumber, "build number should come from the ETW header ProviderVersion");
        StringAssert.Contains(info.OsVersion, "26100", "OsVersion should include the real build");

        // Full BuildLabEx (decoded from the kernel SysConfig/BuildInfo event).
        Assert.AreEqual("26100.3194.amd64fre.ge_release.240331-1435", info.BuildLab, "full BuildLabEx");
        Assert.AreEqual("ge_release", info.Branch, "branch parsed from BuildLabEx");

        // Machine identity from the kernel SystemConfig rundown.
        StringAssert.Contains(info.ProductName, "Windows 10 Pro", "edition from SysConfig/BuildInfo");
        Assert.AreEqual("PORTARE", info.ComputerName, "computer name from SystemConfig/CPU");
        Assert.IsTrue(info.MemorySizeMB > 0, "RAM size from SystemConfig/CPU");
        TestContext.WriteLine($"Edition={info.ProductName}  Computer={info.ComputerName}  RAM={info.MemorySizeMB} MB  Installed={info.InstallDate}");
    }
}
