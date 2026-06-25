using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using FindNeedlePluginLib;
using FindNeedlePluginUtils.StructuredLog;
using PacketDotNet;

namespace PcapPlugin;

/// <summary>
/// Turns one captured packet into a <see cref="StructuredLogResult"/> row: link/network/transport
/// are dissected by PacketDotNet, the app layer (DNS/TLS/HTTP) by <see cref="AppLayerDecoders"/>.
/// The Provider column carries the most specific protocol so the viewer's per-protocol count filters
/// work out of the box; every address/port/host is folded into the searchable text.
/// </summary>
public static class PacketMapper
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public static StructuredLogResult Map(RawPacket raw, string sourceFile, int frameNumber)
    {
        var fields = new Dictionary<string, object?>
        {
            ["frame.number"] = frameNumber,
            ["frame.time"] = raw.Timestamp.ToString("o"),
            ["frame.capturedLength"] = raw.Data.Length,
            ["frame.originalLength"] = raw.OriginalLength,
            ["frame.linkType"] = raw.LinkType,
        };
        var search = new List<string>();
        var result = new StructuredLogResult
        {
            SourceFile = sourceFile,
            LineNumber = frameNumber,
            LogTime = raw.Timestamp,
            Level = Level.Info,
        };

        Packet? packet = null;
        try { packet = Packet.ParsePacket((LinkLayers)raw.LinkType, raw.Data); }
        catch { /* undecodable link type / truncated frame — fall through to a minimal row */ }

        if (packet == null)
        {
            result.Provider = "(raw)";
            result.Message = $"Unparsed frame, {raw.Data.Length} bytes (linktype {raw.LinkType})";
            result.StructuredDataJson = JsonSerializer.Serialize(fields, JsonOpts);
            result.SearchableData = result.Message;
            return result;
        }

        var eth = packet.Extract<EthernetPacket>();
        if (eth != null)
        {
            fields["eth.src"] = eth.SourceHardwareAddress?.ToString();
            fields["eth.dst"] = eth.DestinationHardwareAddress?.ToString();
            fields["eth.type"] = eth.Type.ToString();
        }

        var ip = packet.Extract<IPPacket>();
        var arp = packet.Extract<ArpPacket>();
        var tcp = packet.Extract<TcpPacket>();
        var udp = packet.Extract<UdpPacket>();

        string srcEndpoint, dstEndpoint, protocol, subject = string.Empty;
        Level? severity = null;

        if (ip != null)
        {
            string src = ip.SourceAddress?.ToString() ?? "?";
            string dst = ip.DestinationAddress?.ToString() ?? "?";
            fields["ip.src"] = src;
            fields["ip.dst"] = dst;
            fields["ip.protocol"] = ip.Protocol.ToString();
            fields["ip.ttl"] = ip.TimeToLive;
            search.Add(src); search.Add(dst);

            if (tcp != null)
            {
                srcEndpoint = $"{src}:{tcp.SourcePort}";
                dstEndpoint = $"{dst}:{tcp.DestinationPort}";
                string flags = TcpFlags(tcp);
                fields["tcp.srcPort"] = (int)tcp.SourcePort;
                fields["tcp.dstPort"] = (int)tcp.DestinationPort;
                fields["tcp.flags"] = flags;
                fields["tcp.seq"] = tcp.SequenceNumber;
                fields["tcp.ack"] = tcp.AcknowledgmentNumber;
                fields["tcp.window"] = (int)tcp.WindowSize;
                search.Add(tcp.SourcePort.ToString()); search.Add(tcp.DestinationPort.ToString());

                var app = AppLayerDecoders.TryDecode(tcp.SourcePort, tcp.DestinationPort, tcp.PayloadData, isTcp: true);
                protocol = app?.Protocol ?? "TCP";
                subject = app?.Summary ?? (flags.Length > 0 ? $"[{flags}]" : "");
                ApplyApp(app, fields, search, ref severity);
                if (tcp.Reset) severity ??= Level.Warning;
            }
            else if (udp != null)
            {
                srcEndpoint = $"{src}:{udp.SourcePort}";
                dstEndpoint = $"{dst}:{udp.DestinationPort}";
                fields["udp.srcPort"] = (int)udp.SourcePort;
                fields["udp.dstPort"] = (int)udp.DestinationPort;
                search.Add(udp.SourcePort.ToString()); search.Add(udp.DestinationPort.ToString());

                var app = AppLayerDecoders.TryDecode(udp.SourcePort, udp.DestinationPort, udp.PayloadData, isTcp: false);
                protocol = app?.Protocol ?? "UDP";
                subject = app?.Summary ?? "";
                ApplyApp(app, fields, search, ref severity);
            }
            else
            {
                srcEndpoint = src;
                dstEndpoint = dst;
                protocol = ip.Protocol.ToString().ToUpperInvariant();
                if (TryIcmp(packet, fields, ref subject, ref severity, out var icmpProto))
                    protocol = icmpProto;
            }
        }
        else if (arp != null)
        {
            string sender = arp.SenderProtocolAddress?.ToString() ?? "?";
            string target = arp.TargetProtocolAddress?.ToString() ?? "?";
            protocol = "ARP";
            srcEndpoint = sender;
            dstEndpoint = target;
            subject = arp.Operation == ArpOperation.Request
                ? $"who-has {target} tell {sender}"
                : $"{sender} is-at {arp.SenderHardwareAddress}";
            fields["arp.operation"] = arp.Operation.ToString();
            fields["arp.sender"] = sender;
            fields["arp.target"] = target;
            search.Add(sender); search.Add(target);
        }
        else
        {
            protocol = eth?.Type.ToString() ?? "(other)";
            srcEndpoint = eth?.SourceHardwareAddress?.ToString() ?? "?";
            dstEndpoint = eth?.DestinationHardwareAddress?.ToString() ?? "?";
        }

        result.Provider = protocol;
        result.TaskName = subject;
        result.Message = BuildMessage(srcEndpoint, dstEndpoint, protocol, subject, raw.OriginalLength);
        if (severity != null) result.Level = severity.Value;

        search.Insert(0, result.Message);
        result.SearchableData = string.Join(" ", search);
        result.StructuredDataJson = JsonSerializer.Serialize(fields, JsonOpts);
        return result;
    }

    private static void ApplyApp(AppLayerInfo? app, Dictionary<string, object?> fields, List<string> search, ref Level? severity)
    {
        if (app == null) return;
        foreach (var kv in app.Fields)
        {
            fields[kv.Key] = kv.Value;
            if (kv.Value is string s && s.Length > 0) search.Add(s);
        }
        if (app.Severity != null) severity ??= app.Severity;
    }

    private static bool TryIcmp(Packet packet, Dictionary<string, object?> fields, ref string subject, ref Level? severity, out string protocol)
    {
        var icmp4 = packet.Extract<IcmpV4Packet>();
        if (icmp4 != null)
        {
            protocol = "ICMP";
            string tc = icmp4.TypeCode.ToString();
            subject = tc;
            fields["icmp.typeCode"] = tc;
            if (tc.Contains("Unreachable") || tc.Contains("TimeExceeded") || tc.Contains("Redirect"))
                severity ??= Level.Warning;
            return true;
        }
        var icmp6 = packet.Extract<IcmpV6Packet>();
        if (icmp6 != null)
        {
            protocol = "ICMPv6";
            subject = icmp6.Type.ToString();
            fields["icmpv6.type"] = icmp6.Type.ToString();
            return true;
        }
        protocol = string.Empty;
        return false;
    }

    private static string BuildMessage(string src, string dst, string protocol, string subject, int len)
    {
        var sb = new StringBuilder();
        sb.Append(src).Append(" → ").Append(dst).Append("  ").Append(protocol);
        if (!string.IsNullOrEmpty(subject)) sb.Append("  ").Append(subject);
        sb.Append("  len=").Append(len);
        return sb.ToString();
    }

    private static string TcpFlags(TcpPacket t)
    {
        var f = new List<string>(6);
        if (t.Synchronize) f.Add("SYN");
        if (t.Acknowledgment) f.Add("ACK");
        if (t.Push) f.Add("PSH");
        if (t.Finished) f.Add("FIN");
        if (t.Reset) f.Add("RST");
        if (t.Urgent) f.Add("URG");
        return string.Join(", ", f);
    }
}
