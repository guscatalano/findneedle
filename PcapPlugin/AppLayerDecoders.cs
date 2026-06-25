using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using FindNeedlePluginLib;

namespace PcapPlugin;

/// <summary>What an app-layer decoder pulled out of a TCP/UDP payload.</summary>
public sealed class AppLayerInfo
{
    /// <summary>Protocol label used for the Provider column (e.g. "DNS", "TLS", "HTTP").</summary>
    public string Protocol = string.Empty;
    /// <summary>One-line human subject (e.g. "A example.com", "GET example.com/index", "ClientHello SNI=…").</summary>
    public string Summary = string.Empty;
    /// <summary>Structured fields folded into the details JSON and the searchable text.</summary>
    public Dictionary<string, object?> Fields = new();
    /// <summary>Optional severity bump (e.g. HTTP 5xx, TLS alert) — null leaves the row at Info.</summary>
    public Level? Severity;
}

/// <summary>
/// Deep app-layer dissection for the common protocols PacketDotNet doesn't fully decode: DNS, TLS
/// (ClientHello SNI / handshake type), and HTTP request/response lines. Everything here is strictly
/// best-effort and bounds-checked — a malformed or truncated payload returns null, never throws.
/// </summary>
public static class AppLayerDecoders
{
    /// <summary>Try every decoder appropriate for the ports; returns null if nothing recognized.</summary>
    public static AppLayerInfo? TryDecode(int srcPort, int dstPort, byte[] payload, bool isTcp)
    {
        if (payload == null || payload.Length == 0) return null;
        try
        {
            bool dns = srcPort is 53 or 5353 || dstPort is 53 or 5353;
            bool tls = srcPort is 443 or 8443 || dstPort is 443 or 8443 || LooksLikeTls(payload);
            bool http = srcPort is 80 or 8080 or 8000 || dstPort is 80 or 8080 or 8000 || LooksLikeHttp(payload);

            if (dns) { var d = DecodeDns(payload); if (d != null) return d; }
            if (tls) { var t = DecodeTls(payload); if (t != null) return t; }
            if (http && isTcp) { var h = DecodeHttp(payload); if (h != null) return h; }

            // Fall back to a port-based label even when we couldn't parse the body.
            if (dns) return new AppLayerInfo { Protocol = "DNS", Summary = "DNS" };
            if (tls) return new AppLayerInfo { Protocol = "TLS", Summary = "TLS" };
            if (http) return new AppLayerInfo { Protocol = "HTTP", Summary = "HTTP" };
        }
        catch { /* best-effort: never let a decoder throw */ }
        return null;
    }

    // ---- DNS ----------------------------------------------------------------------------------

    private static AppLayerInfo? DecodeDns(byte[] b)
    {
        if (b.Length < 12) return null;
        int flags = (b[2] << 8) | b[3];
        bool isResponse = (flags & 0x8000) != 0;
        int rcode = flags & 0x000F;
        int qd = (b[4] << 8) | b[5];
        int an = (b[6] << 8) | b[7];
        if (qd == 0 && an == 0) return null;

        int pos = 12;
        var questions = new List<string>();
        var qtypes = new List<string>();
        for (int i = 0; i < qd && pos < b.Length; i++)
        {
            string name = ReadDnsName(b, ref pos);
            if (pos + 4 > b.Length) break;
            int qtype = (b[pos] << 8) | b[pos + 1];
            pos += 4; // qtype(2) + qclass(2)
            questions.Add(name);
            qtypes.Add(DnsType(qtype));
        }

        var answers = new List<string>();
        for (int i = 0; i < an && pos < b.Length; i++)
        {
            ReadDnsName(b, ref pos); // owner name (often a pointer)
            if (pos + 10 > b.Length) break;
            int type = (b[pos] << 8) | b[pos + 1];
            int rdlen = (b[pos + 8] << 8) | b[pos + 9];
            pos += 10;
            if (pos + rdlen > b.Length) break;
            answers.Add(FormatRdata(b, pos, type, rdlen));
            pos += rdlen;
        }

        string qname = questions.FirstOrDefault() ?? string.Empty;
        string qt = qtypes.FirstOrDefault() ?? string.Empty;
        string summary = isResponse
            ? $"response {qt} {qname}" + (answers.Count > 0 ? " → " + string.Join(", ", answers.Take(4)) : "")
            : $"query {qt} {qname}";

        var info = new AppLayerInfo
        {
            Protocol = "DNS",
            Summary = summary.Trim(),
            Fields =
            {
                ["dns.type"] = isResponse ? "response" : "query",
                ["dns.query"] = qname,
                ["dns.qtype"] = qt,
            },
        };
        if (questions.Count > 1) info.Fields["dns.queries"] = questions;
        if (answers.Count > 0) info.Fields["dns.answers"] = answers;
        if (isResponse && rcode != 0) { info.Fields["dns.rcode"] = rcode; info.Severity = Level.Warning; }
        return info;
    }

    private static string ReadDnsName(byte[] b, ref int pos)
    {
        var sb = new StringBuilder();
        int safety = 0;
        int p = pos;
        bool jumped = false;
        int afterPointer = -1;
        while (p < b.Length && safety++ < 128)
        {
            int len = b[p];
            if (len == 0) { p++; break; }
            if ((len & 0xC0) == 0xC0) // compression pointer
            {
                if (p + 1 >= b.Length) break;
                int ptr = ((len & 0x3F) << 8) | b[p + 1];
                if (!jumped) afterPointer = p + 2;
                jumped = true;
                p = ptr;
                continue;
            }
            p++;
            if (p + len > b.Length) break;
            if (sb.Length > 0) sb.Append('.');
            sb.Append(Encoding.ASCII.GetString(b, p, len));
            p += len;
        }
        pos = jumped && afterPointer >= 0 ? afterPointer : p;
        return sb.ToString();
    }

    private static string FormatRdata(byte[] b, int pos, int type, int rdlen)
    {
        try
        {
            switch (type)
            {
                case 1 when rdlen == 4:   // A
                    return new IPAddress(new[] { b[pos], b[pos + 1], b[pos + 2], b[pos + 3] }).ToString();
                case 28 when rdlen == 16: // AAAA
                    var v6 = new byte[16]; Array.Copy(b, pos, v6, 0, 16);
                    return new IPAddress(v6).ToString();
                case 5:                   // CNAME
                case 2:                   // NS
                case 12:                  // PTR
                    int p = pos; return ReadDnsName(b, ref p);
                default:
                    return DnsType(type);
            }
        }
        catch { return DnsType(type); }
    }

    private static string DnsType(int t) => t switch
    {
        1 => "A", 2 => "NS", 5 => "CNAME", 6 => "SOA", 12 => "PTR", 15 => "MX",
        16 => "TXT", 28 => "AAAA", 33 => "SRV", 43 => "DS", 65 => "HTTPS", 255 => "ANY",
        _ => $"TYPE{t}",
    };

    // ---- TLS ----------------------------------------------------------------------------------

    private static bool LooksLikeTls(byte[] b)
    {
        if (b.Length < 3) return false;
        // content type 20-23 (CCS/Alert/Handshake/AppData), version major 3.
        return b[0] >= 0x14 && b[0] <= 0x17 && b[1] == 0x03;
    }

    private static AppLayerInfo? DecodeTls(byte[] b)
    {
        if (b.Length < 5 || b[1] != 0x03) return null;
        int contentType = b[0];
        string version = TlsVersion((b[1] << 8) | b[2]);

        if (contentType == 0x15) // Alert
            return new AppLayerInfo
            {
                Protocol = "TLS",
                Summary = "Alert",
                Severity = Level.Warning,
                Fields = { ["tls.record"] = "alert", ["tls.version"] = version },
            };

        if (contentType != 0x16) // not a handshake
            return new AppLayerInfo
            {
                Protocol = "TLS",
                Summary = contentType == 0x17 ? "Application Data" : "record",
                Fields = { ["tls.version"] = version },
            };

        // Handshake
        if (b.Length < 6) return null;
        int hsType = b[5];
        string hsName = hsType switch { 1 => "ClientHello", 2 => "ServerHello", 11 => "Certificate", 16 => "ClientKeyExchange", _ => $"Handshake({hsType})" };
        var info = new AppLayerInfo
        {
            Protocol = "TLS",
            Summary = hsName,
            Fields = { ["tls.handshake"] = hsName, ["tls.version"] = version },
        };

        if (hsType == 1) // ClientHello — pull the SNI
        {
            string? sni = ExtractSni(b);
            if (!string.IsNullOrEmpty(sni))
            {
                info.Summary = $"ClientHello SNI={sni}";
                info.Fields["tls.sni"] = sni;
            }
        }
        return info;
    }

    // Walk a TLS ClientHello to the server_name extension and return the host. Heavily bounds-checked.
    private static string? ExtractSni(byte[] b)
    {
        int p = 5;                       // skip TLS record header
        if (p + 4 > b.Length) return null;
        p += 4;                          // handshake type(1) + length(3)
        p += 2;                          // client version
        p += 32;                         // random
        if (p >= b.Length) return null;
        int sidLen = b[p]; p += 1 + sidLen;
        if (p + 2 > b.Length) return null;
        int csLen = (b[p] << 8) | b[p + 1]; p += 2 + csLen;
        if (p >= b.Length) return null;
        int compLen = b[p]; p += 1 + compLen;
        if (p + 2 > b.Length) return null;
        int extTotal = (b[p] << 8) | b[p + 1]; p += 2;
        int extEnd = Math.Min(b.Length, p + extTotal);
        while (p + 4 <= extEnd)
        {
            int type = (b[p] << 8) | b[p + 1];
            int len = (b[p + 2] << 8) | b[p + 3];
            p += 4;
            if (type == 0x0000) // server_name
            {
                if (p + 5 > b.Length) return null;
                int nameType = b[p + 2];
                int nameLen = (b[p + 3] << 8) | b[p + 4];
                if (nameType == 0 && p + 5 + nameLen <= b.Length)
                    return Encoding.ASCII.GetString(b, p + 5, nameLen);
                return null;
            }
            p += len;
        }
        return null;
    }

    private static string TlsVersion(int v) => v switch
    {
        0x0301 => "TLS 1.0", 0x0302 => "TLS 1.1", 0x0303 => "TLS 1.2", 0x0304 => "TLS 1.3",
        0x0300 => "SSL 3.0", _ => $"0x{v:X4}",
    };

    // ---- HTTP ---------------------------------------------------------------------------------

    private static readonly string[] HttpMethods =
        { "GET ", "POST ", "PUT ", "DELETE ", "HEAD ", "OPTIONS ", "PATCH ", "TRACE ", "CONNECT " };

    private static bool LooksLikeHttp(byte[] b)
    {
        if (b.Length < 5) return false;
        var head = Encoding.ASCII.GetString(b, 0, Math.Min(b.Length, 8));
        return head.StartsWith("HTTP/") || HttpMethods.Any(m => head.StartsWith(m, StringComparison.Ordinal));
    }

    private static AppLayerInfo? DecodeHttp(byte[] b)
    {
        // Read up to the header block (or first 8 KB) as ASCII.
        int max = Math.Min(b.Length, 8192);
        string text = Encoding.ASCII.GetString(b, 0, max);
        int firstNl = text.IndexOf('\n');
        if (firstNl < 0) return null;
        string requestLine = text.Substring(0, firstNl).TrimEnd('\r');

        if (requestLine.StartsWith("HTTP/")) // response
        {
            var parts = requestLine.Split(' ', 3);
            int status = parts.Length > 1 && int.TryParse(parts[1], out var s) ? s : 0;
            var info = new AppLayerInfo
            {
                Protocol = "HTTP",
                Summary = $"{status} {(parts.Length > 2 ? parts[2] : "")}".Trim(),
                Fields = { ["http.kind"] = "response", ["http.status"] = status },
            };
            if (status >= 400) info.Severity = Level.Warning;
            return info;
        }

        var rl = requestLine.Split(' ');
        if (rl.Length < 2 || !HttpMethods.Any(m => requestLine.StartsWith(m, StringComparison.Ordinal)))
            return null;
        string method = rl[0];
        string target = rl[1];
        string? host = FindHeader(text, "Host");
        string display = host != null ? $"{method} {host}{target}" : $"{method} {target}";
        return new AppLayerInfo
        {
            Protocol = "HTTP",
            Summary = display,
            Fields =
            {
                ["http.kind"] = "request",
                ["http.method"] = method,
                ["http.target"] = target,
                ["http.host"] = host,
            },
        };
    }

    private static string? FindHeader(string text, string name)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0) break; // end of headers
            int colon = trimmed.IndexOf(':');
            if (colon > 0 && trimmed.AsSpan(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return trimmed[(colon + 1)..].Trim();
        }
        return null;
    }
}
