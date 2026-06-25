using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FindNeedlePluginLib;
using PcapPlugin;

namespace PcapPluginTests;

[TestClass]
public class PcapLogProcessorTests
{
    private static List<ISearchResult> Load(string path)
    {
        var p = new PcapLogProcessor();
        p.OpenFile(path);
        p.LoadInMemory();
        return p.GetResults();
    }

    private static string ClassicSample() => PcapTestData.WriteClassicPcap(PcapTestData.SampleFrames());
    private static string PcapNgSample() => PcapTestData.WritePcapNg(PcapTestData.SampleFrames());

    [TestMethod]
    public void RegistersForPcapExtensions()
    {
        var ext = new PcapLogProcessor().RegisterForExtensions();
        CollectionAssert.Contains(ext, ".pcap");
        CollectionAssert.Contains(ext, ".pcapng");
        CollectionAssert.Contains(ext, ".cap");
    }

    [TestMethod]
    public void ParsesEveryPacket_Classic()
    {
        var path = ClassicSample();
        try { Assert.AreEqual(4, Load(path).Count, "Expected one row per packet."); }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ParsesEveryPacket_PcapNg()
    {
        var path = PcapNgSample();
        try { Assert.AreEqual(4, Load(path).Count, "pcapng should yield the same packets as classic."); }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ClassicAndPcapNg_DecodeIdentically()
    {
        string c = ClassicSample(), n = PcapNgSample();
        try
        {
            var rc = Load(c); var rn = Load(n);
            Assert.AreEqual(rc.Count, rn.Count);
            for (int i = 0; i < rc.Count; i++)
                Assert.AreEqual(rc[i].GetMessage(), rn[i].GetMessage(), $"row {i} differs between containers");
        }
        finally { File.Delete(c); File.Delete(n); }
    }

    [TestMethod]
    public void DecodesDnsQueryName()
    {
        var path = ClassicSample();
        try
        {
            var dns = Load(path).First(r => r.GetSource() == "DNS");
            StringAssert.Contains(dns.GetSearchableData(), "example.com");
            StringAssert.Contains(dns.GetStructuredData(), "dns.query");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void DecodesTlsSni()
    {
        var path = ClassicSample();
        try
        {
            var tls = Load(path).First(r => r.GetSource() == "TLS");
            StringAssert.Contains(tls.GetSearchableData(), "test.example.com");
            StringAssert.Contains(tls.GetMessage(), "SNI=test.example.com");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void DecodesHttpRequestLine()
    {
        var path = ClassicSample();
        try
        {
            var http = Load(path).First(r => r.GetSource() == "HTTP");
            StringAssert.Contains(http.GetMessage(), "GET www.example.com/index.html");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void TcpSynRowCarriesFlagsAndEndpoints()
    {
        var path = ClassicSample();
        try
        {
            // The bare SYN (no payload) stays labelled TCP with a [SYN] subject.
            var syn = Load(path).First(r => r.GetSource() == "TCP");
            StringAssert.Contains(syn.GetMessage(), "192.168.1.10:51520");
            StringAssert.Contains(syn.GetMessage(), "93.184.216.34:443");
            StringAssert.Contains(syn.GetMessage(), "SYN");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void TimestampsArePreserved()
    {
        var path = ClassicSample();
        try
        {
            var rows = Load(path);
            Assert.AreEqual(new DateTime(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc), rows[0].GetLogTime().ToUniversalTime());
            Assert.IsTrue(rows[3].GetLogTime() > rows[0].GetLogTime(), "later frames should have later timestamps");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void ProviderCountsArePerProtocol()
    {
        var path = ClassicSample();
        try
        {
            var p = new PcapLogProcessor();
            p.OpenFile(path);
            p.LoadInMemory();
            var counts = p.GetProviderCount();
            Assert.IsTrue(counts.ContainsKey("DNS"));
            Assert.IsTrue(counts.ContainsKey("TLS"));
            Assert.IsTrue(counts.ContainsKey("HTTP"));
            Assert.AreEqual(4, counts.Values.Sum());
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void DecodesDnsAaaaAnswer()
    {
        var path = PcapTestData.WriteClassicPcap(PcapTestData.ExtraProtocolFrames());
        try
        {
            var dns = Load(path).First(r => r.GetSource() == "DNS");
            StringAssert.Contains(dns.GetSearchableData(), "ipv6.example.com");
            StringAssert.Contains(dns.GetSearchableData(), "2001:db8::1"); // AAAA rdata decoded to IPv6
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void DecodesIcmp()
    {
        var path = PcapTestData.WriteClassicPcap(PcapTestData.ExtraProtocolFrames());
        try
        {
            var icmp = Load(path).First(r => r.GetSource() == "ICMP");
            StringAssert.Contains(icmp.GetMessage(), "192.168.1.10");
            StringAssert.Contains(icmp.GetMessage(), "8.8.8.8");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    public void CheckFileFormat_TrueForCapture_FalseForRandomBytes()
    {
        var path = ClassicSample();
        try
        {
            var p = new PcapLogProcessor();
            p.OpenFile(path);
            Assert.IsTrue(p.CheckFileFormat());
        }
        finally { File.Delete(path); }

        var junk = Path.Combine(Path.GetTempPath(), $"notpcap_{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(junk, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 });
        try
        {
            var p2 = new PcapLogProcessor();
            p2.OpenFile(junk);
            Assert.IsFalse(p2.CheckFileFormat());
        }
        finally { File.Delete(junk); }
    }
}
