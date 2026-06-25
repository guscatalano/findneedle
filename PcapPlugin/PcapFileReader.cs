using System;
using System.Collections.Generic;
using System.IO;

namespace PcapPlugin;

/// <summary>One captured packet pulled out of a .pcap / .pcapng container.</summary>
public sealed class RawPacket
{
    /// <summary>Capture time (UTC).</summary>
    public DateTime Timestamp { get; init; }
    /// <summary>libpcap link-layer type (DLT/LINKTYPE) — 1 = Ethernet, 0 = BSD loopback, 101 = raw IP…</summary>
    public int LinkType { get; init; }
    /// <summary>The captured bytes (may be truncated to the capture snaplen).</summary>
    public byte[] Data { get; init; } = Array.Empty<byte>();
    /// <summary>Original on-wire length (≥ Data.Length when the capture was snapped).</summary>
    public int OriginalLength { get; init; }
}

/// <summary>
/// A small, fully-managed reader for both the classic libpcap format (.pcap) and the newer
/// pcapng format (.pcapng). We read the container ourselves — rather than depend on SharpPcap /
/// native libpcap — so the plugin has no native dependency to install on a fresh machine. The raw
/// packet bytes are then handed to PacketDotNet for dissection (see <see cref="PacketMapper"/>).
/// </summary>
public static class PcapFileReader
{
    // Classic pcap magic numbers (first 4 bytes), in the byte order they appear on disk.
    private static readonly byte[] PcapMicroLE = { 0xD4, 0xC3, 0xB2, 0xA1 }; // microsecond, little-endian
    private static readonly byte[] PcapMicroBE = { 0xA1, 0xB2, 0xC3, 0xD4 }; // microsecond, big-endian
    private static readonly byte[] PcapNanoLE  = { 0x4D, 0x3C, 0xB2, 0xA1 }; // nanosecond, little-endian
    private static readonly byte[] PcapNanoBE  = { 0xA1, 0xB2, 0x3C, 0x4D }; // nanosecond, big-endian

    private const uint PcapNgSectionHeaderBlock = 0x0A0D0D0A;

    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>True if the first bytes look like a pcap or pcapng file (used by CheckFileFormat).</summary>
    public static bool LooksLikeCapture(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> magic = stackalloc byte[4];
            if (fs.Read(magic) < 4) return false;
            return MatchesPcapMagic(magic) || IsPcapNgMagic(magic);
        }
        catch { return false; }
    }

    private static bool MatchesPcapMagic(ReadOnlySpan<byte> m) =>
        m.SequenceEqual(PcapMicroLE) || m.SequenceEqual(PcapMicroBE) ||
        m.SequenceEqual(PcapNanoLE) || m.SequenceEqual(PcapNanoBE);

    // pcapng starts with a Section Header Block type 0x0A0D0D0A; the value reads the same in either
    // byte order because the bytes are palindromic (0A 0D 0D 0A).
    private static bool IsPcapNgMagic(ReadOnlySpan<byte> m) =>
        m[0] == 0x0A && m[1] == 0x0D && m[2] == 0x0D && m[3] == 0x0A;

    /// <summary>
    /// Streams every packet in the file lazily (so huge captures don't all sit in memory at once).
    /// Malformed trailing data ends iteration cleanly rather than throwing.
    /// </summary>
    public static IEnumerable<RawPacket> Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var header = new byte[4];
        if (ReadFully(fs, header, 4) < 4) yield break;
        fs.Position = 0;

        if (IsPcapNgMagic(header))
        {
            foreach (var p in ReadPcapNg(fs)) yield return p;
        }
        else if (MatchesPcapMagic(header))
        {
            foreach (var p in ReadClassic(fs)) yield return p;
        }
    }

    // ---- Classic libpcap ----------------------------------------------------------------------

    private static IEnumerable<RawPacket> ReadClassic(Stream fs)
    {
        var global = new byte[24];
        if (ReadFully(fs, global, 24) < 24) yield break;

        bool be = global[0] == 0xA1 && global[1] == 0xB2;             // big-endian on disk
        bool nano = (global[2] == 0x3C && global[3] == 0x4D) ||       // BE nano
                    (global[0] == 0x4D && global[1] == 0x3C);         // LE nano
        int linkType = (int)U32(global, 20, be);

        var recHeader = new byte[16];
        while (ReadFully(fs, recHeader, 16) == 16)
        {
            uint tsSec = U32(recHeader, 0, be);
            uint tsFrac = U32(recHeader, 4, be);   // microseconds or nanoseconds
            uint inclLen = U32(recHeader, 8, be);
            uint origLen = U32(recHeader, 12, be);
            if (inclLen == 0 || inclLen > 256 * 1024 * 1024) yield break; // guard against junk

            var data = new byte[inclLen];
            if (ReadFully(fs, data, (int)inclLen) < inclLen) yield break;

            yield return new RawPacket
            {
                Timestamp = ToTime(tsSec, tsFrac, nano),
                LinkType = linkType,
                Data = data,
                OriginalLength = (int)origLen,
            };
        }
    }

    // ---- pcapng -------------------------------------------------------------------------------

    private static IEnumerable<RawPacket> ReadPcapNg(Stream fs)
    {
        bool be = false;
        // Per-interface link type and timestamp resolution (in seconds per tick). Default 1e-6.
        var ifaceLinkType = new List<int>();
        var ifaceTsResol = new List<double>();

        var blockHead = new byte[8];
        while (ReadFully(fs, blockHead, 8) == 8)
        {
            uint blockType = U32(blockHead, 0, be);
            // The very first block is the Section Header Block; its byte-order magic sets endianness
            // for the whole section. Detect it before trusting `be`.
            if (blockType == PcapNgSectionHeaderBlock)
            {
                // total length is endian-ambiguous here; peek the byte-order magic (next 4 bytes).
                var bom = new byte[4];
                if (ReadFully(fs, bom, 4) < 4) yield break;
                be = bom[0] == 0x1A && bom[1] == 0x2B; // 0x1A2B3C4D big-endian on disk
                uint totalLen = U32(blockHead, 4, be);
                if (totalLen < 12) yield break;
                // we already consumed 8 (head) + 4 (bom); skip the rest of the block body+trailer.
                int remaining = (int)totalLen - 12;
                if (!SkipExactly(fs, remaining)) yield break;
                // new section resets interface tables
                ifaceLinkType.Clear();
                ifaceTsResol.Clear();
                continue;
            }

            uint blockTotalLen = U32(blockHead, 4, be);
            if (blockTotalLen < 12 || blockTotalLen > 256 * 1024 * 1024) yield break;
            int bodyLen = (int)blockTotalLen - 12; // minus type(4)+len(4)+trailing len(4)
            var body = new byte[bodyLen];
            if (ReadFully(fs, body, bodyLen) < bodyLen) yield break;
            if (!SkipExactly(fs, 4)) yield break; // trailing block-total-length

            switch (blockType)
            {
                case 0x00000001: // Interface Description Block
                {
                    int linkType = U16(body, 0, be);
                    double tsResol = ParseTsResol(body, be);
                    ifaceLinkType.Add(linkType);
                    ifaceTsResol.Add(tsResol);
                    break;
                }
                case 0x00000006: // Enhanced Packet Block
                {
                    int ifaceId = (int)U32(body, 0, be);
                    ulong tsHigh = U32(body, 4, be);
                    ulong tsLow = U32(body, 8, be);
                    uint capLen = U32(body, 12, be);
                    uint origLen = U32(body, 16, be);
                    if (capLen > body.Length - 20) break;
                    var data = Slice(body, 20, (int)capLen);
                    double resol = ifaceId < ifaceTsResol.Count ? ifaceTsResol[ifaceId] : 1e-6;
                    int link = ifaceId < ifaceLinkType.Count ? ifaceLinkType[ifaceId] : 1;
                    yield return new RawPacket
                    {
                        Timestamp = ToTime((tsHigh << 32) | tsLow, resol),
                        LinkType = link,
                        Data = data,
                        OriginalLength = (int)origLen,
                    };
                    break;
                }
                case 0x00000003: // Simple Packet Block (no timestamp; interface 0)
                {
                    uint origLen = U32(body, 0, be);
                    int capLen = Math.Min((int)origLen, body.Length - 4);
                    if (capLen < 0) break;
                    var data = Slice(body, 4, capLen);
                    int link = ifaceLinkType.Count > 0 ? ifaceLinkType[0] : 1;
                    yield return new RawPacket
                    {
                        Timestamp = Epoch,
                        LinkType = link,
                        Data = data,
                        OriginalLength = (int)origLen,
                    };
                    break;
                }
                case 0x00000002: // legacy Packet Block (obsolete)
                {
                    int ifaceId = U16(body, 0, be);
                    ulong tsHigh = U32(body, 4, be);
                    ulong tsLow = U32(body, 8, be);
                    uint capLen = U32(body, 12, be);
                    uint origLen = U32(body, 16, be);
                    if (capLen > body.Length - 20) break;
                    var data = Slice(body, 20, (int)capLen);
                    double resol = ifaceId < ifaceTsResol.Count ? ifaceTsResol[ifaceId] : 1e-6;
                    int link = ifaceId < ifaceLinkType.Count ? ifaceLinkType[ifaceId] : 1;
                    yield return new RawPacket
                    {
                        Timestamp = ToTime((tsHigh << 32) | tsLow, resol),
                        LinkType = link,
                        Data = data,
                        OriginalLength = (int)origLen,
                    };
                    break;
                }
                // other block types (name resolution, stats, …) are skipped.
            }
        }
    }

    // if_tsresol option (code 9): one byte. MSB clear → 10^-value seconds; MSB set → 2^-value.
    private static double ParseTsResol(byte[] body, bool be)
    {
        // options start after the fixed 8 bytes (linktype 2 + reserved 2 + snaplen 4).
        int p = 8;
        while (p + 4 <= body.Length)
        {
            int code = U16(body, p, be);
            int len = U16(body, p + 2, be);
            p += 4;
            if (code == 0) break;                 // opt_endofopt
            if (code == 9 && len >= 1 && p < body.Length)
            {
                byte v = body[p];
                return (v & 0x80) == 0 ? Math.Pow(10, -(v & 0x7F)) : Math.Pow(2, -(v & 0x7F));
            }
            p += len + ((4 - (len % 4)) % 4);     // options are padded to 4 bytes
        }
        return 1e-6; // default microseconds
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static DateTime ToTime(uint sec, uint frac, bool nano)
    {
        long ticks = nano ? frac / 100 : frac * 10L; // 1 tick = 100ns
        return Epoch.AddSeconds(sec).AddTicks(ticks);
    }

    private static DateTime ToTime(ulong rawTicks, double secondsPerTick)
    {
        double seconds = rawTicks * secondsPerTick;
        long wholeSec = (long)seconds;
        long subTicks = (long)Math.Round((seconds - wholeSec) * TimeSpan.TicksPerSecond);
        return Epoch.AddSeconds(wholeSec).AddTicks(subTicks);
    }

    private static byte[] Slice(byte[] src, int offset, int len)
    {
        var dst = new byte[len];
        Array.Copy(src, offset, dst, 0, len);
        return dst;
    }

    private static ushort U16(byte[] b, int o, bool be) =>
        be ? (ushort)((b[o] << 8) | b[o + 1]) : (ushort)((b[o + 1] << 8) | b[o]);

    private static uint U32(byte[] b, int o, bool be) => be
        ? ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3]
        : ((uint)b[o + 3] << 24) | ((uint)b[o + 2] << 16) | ((uint)b[o + 1] << 8) | b[o];

    private static int ReadFully(Stream s, byte[] buf, int count)
    {
        int total = 0;
        while (total < count)
        {
            int n = s.Read(buf, total, count - total);
            if (n <= 0) break;
            total += n;
        }
        return total;
    }

    private static bool SkipExactly(Stream s, int count)
    {
        if (count <= 0) return true;
        if (s.CanSeek) { s.Position += count; return s.Position <= s.Length; }
        var tmp = new byte[Math.Min(count, 4096)];
        int left = count;
        while (left > 0)
        {
            int n = s.Read(tmp, 0, Math.Min(left, tmp.Length));
            if (n <= 0) return false;
            left -= n;
        }
        return true;
    }
}
