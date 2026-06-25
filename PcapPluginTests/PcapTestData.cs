using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using PacketDotNet;

namespace PcapPluginTests;

/// <summary>
/// Builds tiny, deterministic, PII-free captures at test time (no committed binaries, so nothing to
/// go missing in CI). L2–L4 are constructed with PacketDotNet; L7 payloads (DNS/TLS/HTTP) are
/// hand-crafted so the app-layer decoders have real bytes to parse. Writes both the classic .pcap
/// and the .pcapng container so both reader paths are exercised.
/// </summary>
internal static class PcapTestData
{
    private static readonly PhysicalAddress MacA = PhysicalAddress.Parse("00-11-22-33-44-55");
    private static readonly PhysicalAddress MacB = PhysicalAddress.Parse("66-77-88-99-AA-BB");

    internal sealed record Frame(DateTime Time, byte[] Bytes);

    /// <summary>One frame each of: DNS query (UDP), TCP SYN, TLS ClientHello, HTTP GET.</summary>
    internal static List<Frame> SampleFrames()
    {
        var baseTime = new DateTime(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);
        var frames = new List<Frame>
        {
            new(baseTime,                       UdpFrame("192.168.1.10", "8.8.8.8", 51514, 53, DnsQuery("example.com"))),
            new(baseTime.AddMilliseconds(12),   TcpFrame("192.168.1.10", "93.184.216.34", 51520, 443, Array.Empty<byte>(), syn: true)),
            new(baseTime.AddMilliseconds(25),   TcpFrame("192.168.1.10", "93.184.216.34", 51520, 443, TlsClientHello("test.example.com"), psh: true, ack: true)),
            new(baseTime.AddMilliseconds(40),   TcpFrame("192.168.1.10", "93.184.216.34", 51522, 80, HttpGet("www.example.com", "/index.html"), psh: true, ack: true)),
        };
        return frames;
    }

    private static byte[] UdpFrame(string src, string dst, ushort sp, ushort dp, byte[] payload)
    {
        var udp = new UdpPacket(sp, dp) { PayloadData = payload };
        var ip = new IPv4Packet(IPAddress.Parse(src), IPAddress.Parse(dst)) { PayloadPacket = udp };
        var eth = new EthernetPacket(MacA, MacB, EthernetType.IPv4) { PayloadPacket = ip };
        eth.UpdateCalculatedValues();
        return eth.Bytes;
    }

    private static byte[] TcpFrame(string src, string dst, ushort sp, ushort dp, byte[] payload,
        bool syn = false, bool ack = false, bool psh = false)
    {
        var tcp = new TcpPacket(sp, dp)
        {
            Synchronize = syn,
            Acknowledgment = ack,
            Push = psh,
            WindowSize = 64240,
            PayloadData = payload,
        };
        var ip = new IPv4Packet(IPAddress.Parse(src), IPAddress.Parse(dst)) { PayloadPacket = tcp };
        var eth = new EthernetPacket(MacA, MacB, EthernetType.IPv4) { PayloadPacket = ip };
        eth.UpdateCalculatedValues();
        return eth.Bytes;
    }

    // ---- L7 payloads --------------------------------------------------------------------------

    internal static byte[] DnsQuery(string name)
    {
        var ms = new MemoryStream();
        void U16(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        U16(0x1234);          // id
        U16(0x0100);          // flags: standard query, RD
        U16(1);               // QDCOUNT
        U16(0); U16(0); U16(0); // AN/NS/AR
        foreach (var label in name.Split('.'))
        {
            ms.WriteByte((byte)label.Length);
            var bytes = Encoding.ASCII.GetBytes(label);
            ms.Write(bytes, 0, bytes.Length);
        }
        ms.WriteByte(0);      // root
        U16(1);               // QTYPE A
        U16(1);               // QCLASS IN
        return ms.ToArray();
    }

    internal static byte[] TlsClientHello(string sni)
    {
        var sniBytes = Encoding.ASCII.GetBytes(sni);

        // server_name extension body
        var ext = new MemoryStream();
        void E16(MemoryStream s, int v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        E16(ext, sniBytes.Length + 3);       // server_name_list length
        ext.WriteByte(0x00);                  // name type host_name
        E16(ext, sniBytes.Length);            // name length
        ext.Write(sniBytes, 0, sniBytes.Length);
        var sniExtData = ext.ToArray();

        var hs = new MemoryStream();
        void H16(int v) { hs.WriteByte((byte)(v >> 8)); hs.WriteByte((byte)v); }
        H16(0x0303);                          // client version TLS 1.2
        hs.Write(new byte[32], 0, 32);        // random (zeros)
        hs.WriteByte(0x00);                   // session id length 0
        H16(2); H16(0x002F);                  // cipher suites (1 suite)
        hs.WriteByte(0x01); hs.WriteByte(0x00); // compression methods (null)
        // extensions
        int extLen = 4 + sniExtData.Length;   // ext header(4) + data
        H16(extLen);
        H16(0x0000);                          // extension type server_name
        H16(sniExtData.Length);
        hs.Write(sniExtData, 0, sniExtData.Length);
        var hsBody = hs.ToArray();

        var rec = new MemoryStream();
        void R16(int v) { rec.WriteByte((byte)(v >> 8)); rec.WriteByte((byte)v); }
        rec.WriteByte(0x16);                  // content type handshake
        R16(0x0301);                          // record version TLS 1.0
        R16(hsBody.Length + 4);               // record length
        rec.WriteByte(0x01);                  // handshake type ClientHello
        rec.WriteByte(0x00);                  // length (3 bytes)
        R16(hsBody.Length);
        rec.Write(hsBody, 0, hsBody.Length);
        return rec.ToArray();
    }

    internal static byte[] HttpGet(string host, string path) =>
        Encoding.ASCII.GetBytes($"GET {path} HTTP/1.1\r\nHost: {host}\r\nUser-Agent: test\r\n\r\n");

    /// <summary>A DNS response with a single AAAA answer (exercises IPv6 rdata decoding).</summary>
    internal static byte[] DnsResponseAAAA(string name, string ipv6)
    {
        var ms = new MemoryStream();
        void U16(int v) { ms.WriteByte((byte)(v >> 8)); ms.WriteByte((byte)v); }
        U16(0x1234);              // id
        U16(0x8180);              // flags: response, RD, RA
        U16(1); U16(1); U16(0); U16(0); // QD=1, AN=1, NS=0, AR=0
        foreach (var label in name.Split('.'))
        {
            ms.WriteByte((byte)label.Length);
            var b = Encoding.ASCII.GetBytes(label); ms.Write(b, 0, b.Length);
        }
        ms.WriteByte(0);          // root
        U16(28); U16(1);          // QTYPE AAAA, QCLASS IN
        // answer: name pointer to the question name at offset 12
        ms.WriteByte(0xC0); ms.WriteByte(0x0C);
        U16(28); U16(1);          // type AAAA, class IN
        U16(0); U16(60);          // TTL (4 bytes) = 60
        var addr = System.Net.IPAddress.Parse(ipv6).GetAddressBytes(); // 16 bytes
        U16(addr.Length);         // RDLENGTH = 16
        ms.Write(addr, 0, addr.Length);
        return ms.ToArray();
    }

    /// <summary>Ethernet/IPv4/ICMP echo-request frame (exercises the ICMP branch of the mapper).</summary>
    internal static byte[] IcmpEchoFrame(string src, string dst)
    {
        var ip = new IPv4Packet(IPAddress.Parse(src), IPAddress.Parse(dst))
        {
            Protocol = ProtocolType.Icmp,
            PayloadData = new byte[] { 8, 0, 0, 0, 0, 1, 0, 1 }, // type=8 (echo request), code=0, checksum, id, seq
        };
        var eth = new EthernetPacket(MacA, MacB, EthernetType.IPv4) { PayloadPacket = ip };
        eth.UpdateCalculatedValues();
        return eth.Bytes;
    }

    /// <summary>A DNS-AAAA response (UDP) + an ICMP echo — the protocol paths the SampleFrames set omits.</summary>
    internal static List<Frame> ExtraProtocolFrames()
    {
        var t = new DateTime(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);
        return new List<Frame>
        {
            new(t, UdpFrame("8.8.8.8", "192.168.1.10", 53, 51514, DnsResponseAAAA("ipv6.example.com", "2001:db8::1"))),
            new(t.AddMilliseconds(5), IcmpEchoFrame("192.168.1.10", "8.8.8.8")),
        };
    }

    // ---- containers ---------------------------------------------------------------------------

    /// <summary>Write a classic libpcap (.pcap), little-endian, microsecond, Ethernet linktype.</summary>
    internal static string WriteClassicPcap(IReadOnlyList<Frame> frames)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcaptest_{Guid.NewGuid():N}.pcap");
        using var fs = File.Create(path);
        Write(fs, new byte[] { 0xD4, 0xC3, 0xB2, 0xA1 });      // magic LE micro
        WriteU16(fs, 2); WriteU16(fs, 4);                       // version 2.4
        WriteU32(fs, 0); WriteU32(fs, 0);                       // thiszone, sigfigs
        WriteU32(fs, 65535);                                    // snaplen
        WriteU32(fs, 1);                                        // network = Ethernet
        foreach (var f in frames)
        {
            var (sec, micros) = UnixParts(f.Time);
            WriteU32(fs, sec);
            WriteU32(fs, (uint)micros);
            WriteU32(fs, (uint)f.Bytes.Length);
            WriteU32(fs, (uint)f.Bytes.Length);
            fs.Write(f.Bytes, 0, f.Bytes.Length);
        }
        return path;
    }

    /// <summary>Write a pcapng (.pcapng): SHB + IDB + one Enhanced Packet Block per frame, LE.</summary>
    internal static string WritePcapNg(IReadOnlyList<Frame> frames)
    {
        var path = Path.Combine(Path.GetTempPath(), $"pcaptest_{Guid.NewGuid():N}.pcapng");
        using var fs = File.Create(path);

        // Section Header Block
        WriteU32(fs, 0x0A0D0D0A);
        WriteU32(fs, 28);
        WriteU32(fs, 0x1A2B3C4D);          // byte-order magic (LE on disk)
        WriteU16(fs, 1); WriteU16(fs, 0);  // version 1.0
        WriteU32(fs, 0xFFFFFFFF); WriteU32(fs, 0xFFFFFFFF); // section length -1
        WriteU32(fs, 28);

        // Interface Description Block (Ethernet, default micro resolution)
        WriteU32(fs, 0x00000001);
        WriteU32(fs, 20);
        WriteU16(fs, 1); WriteU16(fs, 0);  // linktype Ethernet, reserved
        WriteU32(fs, 65535);               // snaplen
        WriteU32(fs, 20);

        foreach (var f in frames)
        {
            int capLen = f.Bytes.Length;
            int padded = (capLen + 3) & ~3;
            int total = 32 + padded; // 4+4 + iface(4)+tsH(4)+tsL(4)+cap(4)+orig(4) + data + trailer(4)
            ulong micros = (ulong)((f.Time - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).Ticks / 10);
            WriteU32(fs, 0x00000006);
            WriteU32(fs, (uint)total);
            WriteU32(fs, 0);                       // interface id 0
            WriteU32(fs, (uint)(micros >> 32));    // ts high
            WriteU32(fs, (uint)(micros & 0xFFFFFFFF)); // ts low
            WriteU32(fs, (uint)capLen);
            WriteU32(fs, (uint)capLen);
            fs.Write(f.Bytes, 0, capLen);
            for (int i = capLen; i < padded; i++) fs.WriteByte(0);
            WriteU32(fs, (uint)total);
        }
        return path;
    }

    private static (uint sec, long micros) UnixParts(DateTime utc)
    {
        var span = utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        uint sec = (uint)(long)span.TotalSeconds;
        long micros = (span.Ticks / 10) % 1_000_000;
        return (sec, micros);
    }

    private static void Write(Stream s, byte[] b) => s.Write(b, 0, b.Length);
    private static void WriteU16(Stream s, int v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); }
    private static void WriteU32(Stream s, uint v)
    {
        s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8));
        s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24));
    }
}
