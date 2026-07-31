using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace findneedle.Wpp;

/// <summary>
/// Renders a WPP event's message in managed code — the second half of what tracefmt does (the first being
/// <see cref="TmfDatabase"/>). Given a <see cref="TmfEntry"/> (format + typed arg list) and the event's raw
/// argument blob, it decodes each typed argument off the blob and applies the printf-style format string.
///
/// PROTOTYPE scope: implements the numeric/pointer/char/string WPP item types and the common printf
/// conversions (d/i/u/x/X/p/c/s with width + zero-pad). Integer/hex args (ItemLong…) AND string args
/// (ItemString = NUL-terminated ANSI, ItemWString = NUL-terminated UTF-16) are validated end-to-end against
/// real captures. Exotic WPP custom types (!STATUS!, !HRESULT!, !GUID!, !TID!, …) and float specifiers are
/// recognized-but-approximate; see the notes inline. The event→(guid,msgNum,blob) wire read lives elsewhere.
/// </summary>
public static class WppMessageFormatter
{
    /// <summary>Decode <paramref name="argBlob"/> per the entry's arg list, then render the format string.
    /// <paramref name="pointerSize"/> is the trace's pointer width (4 or 8) for ItemPtr/%p.</summary>
    public static string Format(TmfEntry entry, ReadOnlySpan<byte> argBlob, int pointerSize = 8)
    {
        var values = DecodeArgs(entry, argBlob, pointerSize);
        return ApplyFormat(entry.Format, values, pointerSize);
    }

    /// <summary>Decode the typed arguments off the blob into a map of argNumber → CLR value (in blob order).</summary>
    public static Dictionary<int, object> DecodeArgs(TmfEntry entry, ReadOnlySpan<byte> blob, int pointerSize = 8)
    {
        var result = new Dictionary<int, object>();
        int off = 0;
        foreach (var arg in entry.Args)
        {
            object value = ReadOne(arg.TypeName, blob, ref off, pointerSize, entry.Enums);
            result[arg.ArgNumber] = value;
        }
        return result;
    }

    // Read a single WPP-typed argument, advancing off. Unknown/over-run types yield null (rendered as "").
    private static object ReadOne(string type, ReadOnlySpan<byte> b, ref int off, int pointerSize,
        IReadOnlyDictionary<string, Dictionary<uint, string>> enums = null)
    {
        // Enum/flags types carry their value→name table inline, e.g. ItemListByte(Low,APC,DPC) /
        // ItemSetLong(1,2,…). The format spec is %s, so return the rendered string directly.
        if (type.StartsWith("ItemList", StringComparison.Ordinal) || type.StartsWith("ItemSet", StringComparison.Ordinal))
            return ReadListOrSet(type, b, ref off);

        // ItemEnum(Name) / ItemFlagsEnum(Name) reference a #enumv table by name (parsed into `enums`).
        if (type.StartsWith("ItemEnum(", StringComparison.Ordinal) || type.StartsWith("ItemFlagsEnum(", StringComparison.Ordinal))
            return ReadEnum(type, b, ref off, enums);

        switch (type)
        {
            case "ItemChar":
            case "ItemUChar":
                return TryByte(b, ref off, 1, out var c) ? (object)b[off - 1] : null;
            case "ItemShort":
                return TryInt(b, ref off, 2, signed: true, out var s) ? (object)(short)s : null;
            case "ItemUShort":
                return TryInt(b, ref off, 2, signed: false, out var us) ? (object)(ushort)us : null;
            case "ItemLong":
                return TryInt(b, ref off, 4, signed: true, out var l) ? (object)(int)l : null;
            case "ItemULong":
                return TryInt(b, ref off, 4, signed: false, out var ul) ? (object)(uint)ul : null;
            case "ItemLongLong":
            case "ItemQuad":
            case "ItemLongLongX": // %I64x — int64, the format spec renders it as hex
                return TryLong(b, ref off, signed: true, out var ll) ? (object)ll : null;
            case "ItemULongLong":
            case "ItemUQuad":
                return TryLong(b, ref off, signed: false, out var ull) ? (object)(ulong)ull : null;
            case "ItemHRESULT": // 32-bit status; tracepdb rewrites %!HRESULT! -> %N!s!, so return a ready string
                return TryInt(b, ref off, 4, signed: true, out var hr) ? FormatHResult((uint)(int)hr) : null;
            case "ItemNTSTATUS":
                return TryInt(b, ref off, 4, signed: true, out var nt) ? FormatNtStatus((uint)(int)nt) : null;
            case "ItemGuid": // 16 bytes inline, standard GUID binary layout (Data1 LE, Data2 LE, Data3 LE, Data4)
                if (off + 16 > b.Length) { off = b.Length; return null; }
                var guid = new Guid(b.Slice(off, 16)); off += 16; return guid.ToString("D");
            case "ItemPtr":
                if (off + pointerSize > b.Length) return null;
                ulong p = pointerSize == 8 ? BitConverter.ToUInt64(b.Slice(off, 8))
                                           : BitConverter.ToUInt32(b.Slice(off, 4));
                off += pointerSize;
                return p;
            case "ItemFloat":
                if (off + 4 > b.Length) return null;
                var f = BitConverter.ToSingle(b.Slice(off, 4)); off += 4; return f;
            case "ItemDouble":  // tracefmt uses printf %g (6 significant figures)
                if (off + 8 > b.Length) return null;
                var d = BitConverter.ToDouble(b.Slice(off, 8)); off += 8;
                return d.ToString("G6", CultureInfo.InvariantCulture);
            case "ItemString":   // ANSI, NUL-terminated (LPCSTR / %s) — validated against a real capture
            case "ItemRString":  // raw LPCSTR variant — same NUL-terminated ANSI on the wire
                return ReadNulTerminatedString(b, ref off, wide: false);
            case "ItemWString":  // UTF-16, NUL-terminated (LPCWSTR)
            case "ItemRWString": // raw LPCWSTR variant — NUL-terminated UTF-16
                return ReadNulTerminatedString(b, ref off, wide: true);
            case "ItemPString":  // COUNTED ANSI (ANSI_STRING / std::string): USHORT byte-length + bytes, no NUL
                return ReadCountedString(b, ref off, wide: false);
            case "ItemPWString": // COUNTED UTF-16 (UNICODE_STRING / std::wstring)
                return ReadCountedString(b, ref off, wide: true);
            case "ItemCLSID":    // GUID aliases — 16 inline bytes, same layout as ItemGuid
            case "ItemIID":
            case "ItemLIBID":
                if (off + 16 > b.Length) { off = b.Length; return null; }
                var g2 = new Guid(b.Slice(off, 16)); off += 16; return g2.ToString("D");
            case "ItemLongLongXX": // %I64X — int64, spec renders upper-hex
            case "ItemLongLongO":  // %I64o — int64, spec renders octal
                return TryLong(b, ref off, signed: true, out var llx) ? (object)llx : null;
            case "ItemWINERROR":   // Win32 DWORD error — tracefmt: "{decimal}(SYMBOL)"
                return TryInt(b, ref off, 4, signed: false, out var we) ? FormatWinError((uint)we) : null;
            case "ItemNDIS_STATUS": // NDIS_STATUS — tracefmt: "0x{hex}(NDIS_STATUS_...)"
                return TryInt(b, ref off, 4, signed: false, out var ns)
                    ? (NdisStatusTable.TryGetValue((uint)ns, out var nsn) ? $"0x{(uint)ns:x8}({nsn})" : $"0x{(uint)ns:x8}") : null;
            case "ItemNDIS_OID":    // NDIS OID — tracefmt: "0x{hex}(OID_...)"
                return TryInt(b, ref off, 4, signed: false, out var oid)
                    ? (NdisOidTable.TryGetValue((uint)oid, out var on) ? $"0x{(uint)oid:x8}({on})" : $"0x{(uint)oid:x8}") : null;
            case "ItemSid":        // binary SID → canonical "S-1-5-18" (tracefmt resolves to an account name)
                return ReadSid(b, ref off);
            case "ItemIPAddr":     // 4 bytes, a.b.c.d in wire order
                if (off + 4 > b.Length) { off = b.Length; return null; }
                var ip = $"{b[off]}.{b[off + 1]}.{b[off + 2]}.{b[off + 3]}"; off += 4; return ip;
            case "ItemPort":       // 2 bytes, network (big-endian) order
                if (off + 2 > b.Length) { off = b.Length; return null; }
                int port = (b[off] << 8) | b[off + 1]; off += 2; return port.ToString();
            case "ItemChar4":      // 4-byte FourCC → ASCII chars in wire order (e.g. "RGBA")
                if (off + 4 > b.Length) { off = b.Length; return null; }
                var cc = Encoding.ASCII.GetString(b.Slice(off, 4)); off += 4; return cc;
            case "ItemTimestamp":  // 8-byte FILETIME → UTC (tracefmt renders LOCAL time — TZ-dependent; we use UTC)
                if (off + 8 > b.Length) { off = b.Length; return null; }
                var ftv = BitConverter.ToInt64(b.Slice(off, 8)); off += 8; return FormatFileTimeUtc(ftv);
            case "ItemTimeDelta":  // 8-byte 100ns delta → seconds "N.NNNs" (tracefmt form; validated at 5s)
                if (off + 8 > b.Length) { off = b.Length; return null; }
                var dv = BitConverter.ToInt64(b.Slice(off, 8)); off += 8;
                return (dv / 10_000_000.0).ToString("0.000", CultureInfo.InvariantCulture) + "s";
            case "ItemWaitTime":   // 8-byte FILETIME, same as ItemTimestamp → UTC (tracefmt shows LOCAL time)
                if (off + 8 > b.Length) { off = b.Length; return null; }
                var wt = BitConverter.ToInt64(b.Slice(off, 8)); off += 8; return FormatFileTimeUtc(wt);
            case "ItemHexDump":    // USHORT byte-count + raw bytes → hex. (tracefmt renders this empty inline;
                if (off + 2 > b.Length) { off = b.Length; return null; }  // we emit the hex, which is more useful.)
                int hlen = b[off] | (b[off + 1] << 8); off += 2;
                if (hlen < 0 || off + hlen > b.Length) { off = b.Length; return null; }
                var hex = Convert.ToHexString(b.Slice(off, hlen)); off += hlen; return hex;
            default:
                // Unknown item type: we can't know its width, so stop consuming (further args unreadable).
                off = b.Length;
                return null;
        }
    }

    private static bool TryByte(ReadOnlySpan<byte> b, ref int off, int n, out byte v)
    {
        v = 0; if (off + n > b.Length) return false; v = b[off]; off += n; return true;
    }
    private static bool TryInt(ReadOnlySpan<byte> b, ref int off, int n, bool signed, out long v)
    {
        v = 0;
        if (off + n > b.Length) return false;
        long acc = 0;
        for (int i = 0; i < n; i++) acc |= (long)b[off + i] << (8 * i); // little-endian
        if (signed && n < 8)
        {
            long signBit = 1L << (8 * n - 1);
            if ((acc & signBit) != 0) acc -= 1L << (8 * n);
        }
        off += n; v = acc; return true;
    }
    private static bool TryLong(ReadOnlySpan<byte> b, ref int off, bool signed, out long v)
        => TryInt(b, ref off, 8, signed, out v);

    // Counted strings (ItemPString/ItemPWString ← ANSI_STRING/UNICODE_STRING/std::string): a USHORT byte
    // count then the raw bytes, NO NUL terminator — confirmed against a real capture ("\x0B\x00CountedAnsi").
    private static string ReadCountedString(ReadOnlySpan<byte> b, ref int off, bool wide)
    {
        if (off + 2 > b.Length) { off = b.Length; return ""; }
        int byteCount = b[off] | (b[off + 1] << 8);
        off += 2;
        if (byteCount < 0 || off + byteCount > b.Length) { off = b.Length; return ""; }
        var s = wide ? Encoding.Unicode.GetString(b.Slice(off, byteCount))
                     : Encoding.ASCII.GetString(b.Slice(off, byteCount));
        off += byteCount;
        return s;
    }

    // ItemEnum(Name) → the symbol for the value; ItemFlagsEnum(Name) → matching flags joined " | " with a
    // trailing "(0x{value:x})". The name table comes from the TMF's #enumv blocks. Value is a 4-byte ULONG.
    private static string ReadEnum(string type, ReadOnlySpan<byte> b, ref int off,
        IReadOnlyDictionary<string, Dictionary<uint, string>> enums)
    {
        bool isFlags = type.StartsWith("ItemFlagsEnum", StringComparison.Ordinal);
        int paren = type.IndexOf('(');
        int close = type.LastIndexOf(')');
        string enumName = (paren >= 0 && close > paren) ? type.Substring(paren + 1, close - paren - 1).Trim() : "";
        if (off + 4 > b.Length) { off = b.Length; return ""; }
        uint val = BitConverter.ToUInt32(b.Slice(off, 4)); off += 4;

        Dictionary<uint, string> map = null;
        enums?.TryGetValue(enumName, out map);

        if (isFlags)
        {
            if (map == null) return $"0x{val:x}";
            var parts = new List<string>();
            foreach (var kv in map.OrderBy(k => k.Key))
                if (kv.Key != 0 && (val & kv.Key) == kv.Key) parts.Add(kv.Value);
            return parts.Count > 0 ? $"{string.Join(" | ", parts)}(0x{val:x})" : $"0x{val:x}";
        }
        return map != null && map.TryGetValue(val, out var name) ? name : $"0x{val:x}";
    }

    // ItemList*/ItemSet* — the name table is embedded in the type, e.g. "ItemListByte(Low,APC,DPC)" or
    // "ItemSetLong(1,2,…,32)". List: value indexes the names → "0x{value:x8}(name)". Set: each set bit i →
    // names[i], joined "[a,b,…]". Width from the suffix (Byte=1, Short=2, else Long=4). Matches tracefmt.
    private static string ReadListOrSet(string type, ReadOnlySpan<byte> b, ref int off)
    {
        int paren = type.IndexOf('(');
        string baseName = paren >= 0 ? type.Substring(0, paren) : type;
        var names = new List<string>();
        if (paren >= 0)
        {
            int close = type.LastIndexOf(')');
            var inner = type.Substring(paren + 1, (close > paren ? close : type.Length) - paren - 1);
            foreach (var n in inner.Split(',')) names.Add(n.Trim());
        }
        int width = baseName.EndsWith("Byte", StringComparison.Ordinal) ? 1
                  : baseName.EndsWith("Short", StringComparison.Ordinal) ? 2 : 4;
        if (off + width > b.Length) { off = b.Length; return ""; }
        long val = 0;
        for (int i = 0; i < width; i++) val |= (long)b[off + i] << (8 * i);
        off += width;
        uint uval = (uint)val;

        if (baseName.StartsWith("ItemSet", StringComparison.Ordinal))
        {
            var parts = new List<string>();
            for (int bit = 0; bit < names.Count && bit < 32; bit++)
                if ((uval & (1u << bit)) != 0) parts.Add(names[bit]);
            return "[" + string.Join(",", parts) + "]";
        }
        // List: index into names.
        return uval < names.Count ? $"0x{uval:x8}({names[(int)uval]})" : $"0x{uval:x8}";
    }

    // FILETIME (100ns since 1601) → stable UTC string. tracefmt renders LOCAL time in MM/dd/yyyy-HH:mm:ss.fff,
    // which is timezone-dependent; we deliberately emit UTC ISO-ish for a deterministic, portable result.
    private static string FormatFileTimeUtc(long fileTime)
    {
        try { return DateTime.FromFileTimeUtc(fileTime).ToString("yyyy-MM-dd HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture); }
        catch { return fileTime.ToString(CultureInfo.InvariantCulture); }
    }

    // Binary SID → canonical string form "S-<rev>-<idauth>-<sub0>-<sub1>-…" (e.g. S-1-5-18). tracefmt instead
    // resolves it to an account display name via LookupAccountSid (locale/machine-dependent); we render the
    // stable, portable SID string. Layout: Revision, SubAuthorityCount, 6-byte big-endian IdentifierAuthority,
    // then SubAuthorityCount little-endian DWORDs.
    private static string ReadSid(ReadOnlySpan<byte> b, ref int off)
    {
        if (off + 8 > b.Length) { off = b.Length; return ""; }
        int rev = b[off];
        int subCount = b[off + 1];
        long idAuth = 0;
        for (int i = 0; i < 6; i++) idAuth = (idAuth << 8) | b[off + 2 + i];
        int len = 8 + 4 * subCount;
        if (subCount < 0 || off + len > b.Length) { off = b.Length; return ""; }
        var sb = new StringBuilder();
        sb.Append("S-").Append(rev).Append('-').Append(idAuth);
        for (int i = 0; i < subCount; i++)
            sb.Append('-').Append(BitConverter.ToUInt32(b.Slice(off + 8 + 4 * i, 4)));
        off += len;
        return sb.ToString();
    }

    // Complete status/error symbol tables, generated from the SDK ntstatus.h / winerror.h
    // (tools/wpp-symbol-tables/gen.py) and embedded as resources. Loaded once, lazily.
    private static readonly object _tableLock = new();
    private static Dictionary<uint, string> _ntStatus, _win32Err, _hresult, _ndisStatus, _ndisOid;

    private static Dictionary<uint, string> LoadTable(string logicalName, bool hexKey)
    {
        var dict = new Dictionary<uint, string>();
        try
        {
            using var s = typeof(WppMessageFormatter).Assembly.GetManifestResourceStream(logicalName);
            if (s == null) return dict;
            using var r = new StreamReader(s);
            string line;
            while ((line = r.ReadLine()) != null)
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.AsSpan(0, eq);
                uint v = uint.Parse(key, hexKey ? NumberStyles.HexNumber : NumberStyles.Integer, CultureInfo.InvariantCulture);
                dict[v] = line.Substring(eq + 1);
            }
        }
        catch { /* table unavailable → codes render as bare hex/decimal */ }
        return dict;
    }

    private static Dictionary<uint, string> NtStatusTable { get { if (_ntStatus == null) lock (_tableLock) { _ntStatus ??= LoadTable("wpp.ntstatus.txt", true); } return _ntStatus; } }
    private static Dictionary<uint, string> Win32Table { get { if (_win32Err == null) lock (_tableLock) { _win32Err ??= LoadTable("wpp.win32err.txt", false); } return _win32Err; } }
    private static Dictionary<uint, string> HResultTable { get { if (_hresult == null) lock (_tableLock) { _hresult ??= LoadTable("wpp.hresult.txt", true); } return _hresult; } }
    private static Dictionary<uint, string> NdisStatusTable { get { if (_ndisStatus == null) lock (_tableLock) { _ndisStatus ??= LoadTable("wpp.ndisstatus.txt", true); } return _ndisStatus; } }
    private static Dictionary<uint, string> NdisOidTable { get { if (_ndisOid == null) lock (_tableLock) { _ndisOid ??= LoadTable("wpp.ndisoid.txt", true); } return _ndisOid; } }

    private static string FormatNtStatus(uint code)
        => NtStatusTable.TryGetValue(code, out var n) ? $"0x{code:x8}({n})" : $"0x{code:x8}";

    private static string FormatWinError(uint code)
        => Win32Table.TryGetValue(code, out var n) ? $"{code}({n})" : code.ToString(CultureInfo.InvariantCulture);

    private static string FormatHResult(uint hr)
    {
        // A FACILITY_WIN32 error HRESULT (0x8007xxxx) renders as the Win32 error name — matches tracefmt
        // (0x80070005 → ERROR_ACCESS_DENIED, not E_ACCESSDENIED). Otherwise use the HRESULT symbol table.
        if ((hr & 0xFFFF0000) == 0x80070000 && Win32Table.TryGetValue(hr & 0xFFFF, out var we))
            return $"0x{hr:x8}({we})";
        return HResultTable.TryGetValue(hr, out var n) ? $"0x{hr:x8}({n})" : $"0x{hr:x8}";
    }

    // WPP %s args are logged NUL-terminated inline (no length prefix) — confirmed by capturing a real WPP
    // trace with string args and dumping the wire bytes: ItemString = "alpha\0", ItemWString = "root\0\0"
    // (UTF-16). Read up to the terminator and consume it.
    private static string ReadNulTerminatedString(ReadOnlySpan<byte> b, ref int off, bool wide)
    {
        int start = off;
        if (wide)
        {
            while (off + 1 < b.Length && !(b[off] == 0 && b[off + 1] == 0)) off += 2;
            var s = Encoding.Unicode.GetString(b.Slice(start, off - start));
            off = off + 1 < b.Length ? off + 2 : b.Length; // consume the wide NUL
            return s;
        }
        else
        {
            while (off < b.Length && b[off] != 0) off++;
            var s = Encoding.ASCII.GetString(b.Slice(start, off - start));
            if (off < b.Length) off++; // consume the NUL
            return s;
        }
    }

    /// <summary>Apply a WPP printf-style format string with %N!spec! placeholders to the decoded args.
    /// <paramref name="pointerSize"/> sets %p width (uppercase, zero-padded to the pointer width, like tracefmt).</summary>
    public static string ApplyFormat(string format, IReadOnlyDictionary<int, object> args, int pointerSize = 8)
    {
        if (string.IsNullOrEmpty(format)) return "";
        var sb = new StringBuilder(format.Length + 32);
        int i = 0;
        while (i < format.Length)
        {
            char ch = format[i];
            if (ch != '%') { sb.Append(ch); i++; continue; }

            // "%%" → literal percent.
            if (i + 1 < format.Length && format[i + 1] == '%') { sb.Append('%'); i += 2; continue; }

            // "%" + digits = arg number.
            int j = i + 1;
            int numStart = j;
            while (j < format.Length && char.IsDigit(format[j])) j++;
            if (j == numStart) { sb.Append('%'); i++; continue; } // stray % — emit literally

            int argNum = int.Parse(format.AsSpan(numStart, j - numStart), CultureInfo.InvariantCulture);

            // Optional !spec! immediately after the number.
            string spec = null;
            if (j < format.Length && format[j] == '!')
            {
                int close = format.IndexOf('!', j + 1);
                if (close > j) { spec = format.Substring(j + 1, close - j - 1); j = close + 1; }
            }
            i = j;

            if (argNum == 0) continue; // %0 = WPP prefix (provider/time/pid are separate fields) → nothing here
            sb.Append(RenderArg(argNum, spec, args, pointerSize));
        }
        return sb.ToString();
    }

    private static string RenderArg(int argNum, string spec, IReadOnlyDictionary<int, object> args, int pointerSize = 8)
    {
        if (!args.TryGetValue(argNum, out var val) || val == null)
            return ""; // reserved %1..%9 or a missing arg → empty (prototype)

        // Split spec into [flags/width][conversion]. e.g. "d", "x", "08x", "ld", "I64u", "s".
        char conv = spec != null && spec.Length > 0 ? spec[^1] : DefaultConv(val);
        string flagsWidth = spec != null && spec.Length > 0 ? spec[..^1] : "";
        // Strip length modifiers (l, ll, h, I64, w) so only flags/width/precision remain.
        flagsWidth = StripLengthModifiers(flagsWidth);
        ParseWidth(flagsWidth, out bool zeroPad, out int width);

        string s = conv switch
        {
            'd' or 'i' => Convert.ToInt64(val, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            'u' => UnsignedOf(val).ToString(CultureInfo.InvariantCulture),
            'x' => UnsignedOf(val).ToString("x"),
            'X' => UnsignedOf(val).ToString("X"),
            // tracefmt renders %p uppercase, zero-padded to the trace's pointer width (16 hex on x64).
            'p' => UnsignedOf(val).ToString("X").PadLeft(pointerSize * 2, '0'),
            'o' => Convert.ToString((long)UnsignedOf(val), 8),
            'c' => RenderChar(val),
            's' => val.ToString(),
            'f' or 'e' or 'g' or 'F' or 'E' or 'G' => Convert.ToDouble(val, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            _ => val.ToString(),
        };

        if (width > 0 && s.Length < width)
            s = s.PadLeft(width, zeroPad ? '0' : ' ');
        return s;
    }

    private static char DefaultConv(object val) => val switch
    {
        string => 's',
        byte => 'c',
        float or double => 'f',
        _ => 'd',
    };

    private static ulong UnsignedOf(object val) => val switch
    {
        int v => unchecked((uint)v),
        uint v => v,
        long v => unchecked((ulong)v),
        ulong v => v,
        short v => unchecked((ushort)v),
        ushort v => v,
        byte v => v,
        _ => Convert.ToUInt64(val, CultureInfo.InvariantCulture),
    };

    private static string RenderChar(object val)
        => val is byte b ? ((char)b).ToString() : ((char)Convert.ToInt32(val, CultureInfo.InvariantCulture)).ToString();

    private static string StripLengthModifiers(string s)
    {
        // Remove WPP/printf length modifiers, leaving flags/width/precision.
        foreach (var mod in new[] { "I64", "ll", "l", "h", "w", "z", "t" })
            s = s.Replace(mod, "");
        return s;
    }

    private static void ParseWidth(string flagsWidth, out bool zeroPad, out int width)
    {
        zeroPad = false; width = 0;
        if (string.IsNullOrEmpty(flagsWidth)) return;
        int k = 0;
        // flags
        while (k < flagsWidth.Length && (flagsWidth[k] == '-' || flagsWidth[k] == '+' || flagsWidth[k] == ' ' || flagsWidth[k] == '#' || flagsWidth[k] == '0'))
        {
            if (flagsWidth[k] == '0') zeroPad = true;
            k++;
        }
        int wStart = k;
        while (k < flagsWidth.Length && char.IsDigit(flagsWidth[k])) k++;
        if (k > wStart) int.TryParse(flagsWidth.AsSpan(wStart, k - wStart), out width);
    }
}
