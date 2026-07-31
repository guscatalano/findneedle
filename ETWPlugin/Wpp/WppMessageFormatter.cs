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
        return ApplyFormat(entry.Format, values);
    }

    /// <summary>Decode the typed arguments off the blob into a map of argNumber → CLR value (in blob order).</summary>
    public static Dictionary<int, object> DecodeArgs(TmfEntry entry, ReadOnlySpan<byte> blob, int pointerSize = 8)
    {
        var result = new Dictionary<int, object>();
        int off = 0;
        foreach (var arg in entry.Args)
        {
            object value = ReadOne(arg.TypeName, blob, ref off, pointerSize);
            result[arg.ArgNumber] = value;
        }
        return result;
    }

    // Read a single WPP-typed argument, advancing off. Unknown/over-run types yield null (rendered as "").
    private static object ReadOne(string type, ReadOnlySpan<byte> b, ref int off, int pointerSize)
    {
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
                return TryLong(b, ref off, signed: true, out var ll) ? (object)ll : null;
            case "ItemULongLong":
            case "ItemUQuad":
                return TryLong(b, ref off, signed: false, out var ull) ? (object)(ulong)ull : null;
            case "ItemPtr":
                if (off + pointerSize > b.Length) return null;
                ulong p = pointerSize == 8 ? BitConverter.ToUInt64(b.Slice(off, 8))
                                           : BitConverter.ToUInt32(b.Slice(off, 4));
                off += pointerSize;
                return p;
            case "ItemFloat":
                if (off + 4 > b.Length) return null;
                var f = BitConverter.ToSingle(b.Slice(off, 4)); off += 4; return f;
            case "ItemDouble":
                if (off + 8 > b.Length) return null;
                var d = BitConverter.ToDouble(b.Slice(off, 8)); off += 8; return d;
            case "ItemString":   // ANSI, NUL-terminated (validated against a real WppEmitter-style capture)
            case "ItemPString":
                return ReadNulTerminatedString(b, ref off, wide: false);
            case "ItemWString":  // UTF-16, double-NUL-terminated (validated against a real capture)
            case "ItemPWString":
                return ReadNulTerminatedString(b, ref off, wide: true);
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

    /// <summary>Apply a WPP printf-style format string with %N!spec! placeholders to the decoded args.</summary>
    public static string ApplyFormat(string format, IReadOnlyDictionary<int, object> args)
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
            sb.Append(RenderArg(argNum, spec, args));
        }
        return sb.ToString();
    }

    private static string RenderArg(int argNum, string spec, IReadOnlyDictionary<int, object> args)
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
            'p' => UnsignedOf(val).ToString("x"),
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
