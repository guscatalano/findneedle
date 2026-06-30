using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace findneedle.ETWPlugin;

/// <summary>How a native ETW provider was identified, in descending reliability.</summary>
public enum EtwProviderSource
{
    /// <summary>From the binary's WEVT_TEMPLATE (CRIM) resource — the compiled instrumentation
    /// manifest. Authoritative GUID.</summary>
    Manifest,
    /// <summary>TraceLogging / self-describing provider metadata whose name hashes (the standard
    /// name→GUID algorithm) to a GUID that is ALSO present in the binary — name and GUID agree.</summary>
    TraceLoggingVerified,
    /// <summary>TraceLogging / self-describing provider NAME found in metadata; the GUID is the
    /// standard name-derived value but was not separately found in the binary (e.g. the provider
    /// used an explicit, non-name-based GUID we couldn't confirm).</summary>
    TraceLoggingDerived,
    /// <summary>Legacy brute-force guess (a GUID-shaped 16 bytes followed by a pointer to a string).
    /// Low confidence — only used when nothing authoritative was found.</summary>
    Heuristic,
}

/// <summary>One ETW provider discovered in a native binary, with the evidence that identified it.</summary>
public sealed class EtwProviderRef
{
    public Guid Guid { get; init; }
    public string? Name { get; init; }
    public EtwProviderSource Source { get; init; }

    public bool IsAuthoritative => Source is EtwProviderSource.Manifest or EtwProviderSource.TraceLoggingVerified;

    public string SourceLabel => Source switch
    {
        EtwProviderSource.Manifest => "manifest",
        EtwProviderSource.TraceLoggingVerified => "TraceLogging (verified)",
        EtwProviderSource.TraceLoggingDerived => "TraceLogging (name-derived)",
        _ => "heuristic (low confidence)",
    };
}

/// <summary>
/// Extracts ETW providers from a native EXE/DLL by parsing the structures the toolchain actually emits —
/// the WEVT_TEMPLATE manifest resource (authoritative GUIDs) and TraceLogging provider-metadata blobs
/// (real names, GUID confirmed via the name→GUID hash) — instead of guessing from byte shapes. The old
/// GUID+string-pointer heuristic is kept only as a last-resort fallback when nothing authoritative is found.
/// </summary>
public static class EtwNativeProviderScanner
{
    /// <summary>Back-compat tuple API (GUID + name only). Prefer <see cref="Scan"/> for source/confidence.</summary>
    public static List<(Guid guid, string? name)> ExtractNativeEtwProviders(string path)
        => Scan(path).Select(p => (p.Guid, p.Name)).ToList();

    /// <summary>Reliable native ETW provider scan. Never throws for a valid PE; returns whatever it found.</summary>
    public static List<EtwProviderRef> Scan(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"File not found: {path}");
        var bytes = File.ReadAllBytes(path);
        var pe = PeImage.Parse(bytes); // throws InvalidDataException on a non-PE

        // GUID -> best result so far. Higher-confidence sources win; a result with no name can be
        // upgraded with a name from a lower-confidence sighting of the same GUID.
        var byGuid = new Dictionary<Guid, EtwProviderRef>();
        void Consider(EtwProviderRef r)
        {
            if (r.Guid == Guid.Empty) return;
            if (byGuid.TryGetValue(r.Guid, out var existing))
            {
                if (Rank(existing.Source) >= Rank(r.Source))
                {
                    if (string.IsNullOrEmpty(existing.Name) && !string.IsNullOrEmpty(r.Name))
                        byGuid[r.Guid] = new EtwProviderRef { Guid = r.Guid, Name = r.Name, Source = existing.Source };
                    return;
                }
            }
            byGuid[r.Guid] = r;
        }

        // 1. Manifest providers — authoritative GUIDs from the WEVT_TEMPLATE (CRIM) resource.
        try
        {
            foreach (var g in ParseManifestProviderGuids(pe))
                Consider(new EtwProviderRef { Guid = g, Name = ResolvePublisherName(g), Source = EtwProviderSource.Manifest });
        }
        catch { /* malformed resource — skip, other paths still run */ }

        // 2. TraceLogging / self-describing providers — real names from the metadata blobs; confirm the
        //    GUID by recomputing it from the name and checking the bytes are present in the binary.
        try
        {
            foreach (var name in ScanTraceLoggingProviderNames(pe))
            {
                var g = GuidFromProviderName(name);
                bool verified = ContainsBytes(bytes, g.ToByteArray());
                // Unverified (name-derived) hits are the riskier ones: keep them only when the name is
                // namespaced (Microsoft.Windows.X / Company-Component), which real providers almost always
                // are — a bare single token that didn't verify is far more likely to be a stray string.
                if (!verified && !(name.Contains('.') || name.Contains('-'))) continue;
                Consider(new EtwProviderRef
                {
                    Guid = g,
                    Name = name,
                    Source = verified ? EtwProviderSource.TraceLoggingVerified : EtwProviderSource.TraceLoggingDerived,
                });
            }
        }
        catch { /* skip */ }

        // 3. Heuristic fallback — ONLY when the authoritative passes found nothing, since it is noisy.
        if (byGuid.Count == 0)
        {
            try
            {
                foreach (var (g, n) in HeuristicScan(pe))
                    Consider(new EtwProviderRef { Guid = g, Name = n, Source = EtwProviderSource.Heuristic });
            }
            catch { /* skip */ }
        }

        return byGuid.Values
            .OrderByDescending(p => Rank(p.Source))
            .ThenBy(p => p.Name ?? p.Guid.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int Rank(EtwProviderSource s) => s switch
    {
        EtwProviderSource.Manifest => 3,
        EtwProviderSource.TraceLoggingVerified => 2,
        EtwProviderSource.TraceLoggingDerived => 1,
        _ => 0,
    };

    // ---- Manifest providers: WEVT_TEMPLATE (CRIM) resource --------------------------------------

    private static IEnumerable<Guid> ParseManifestProviderGuids(PeImage pe)
    {
        var guids = new List<Guid>();
        if (pe.ResourceRva == 0) return guids;
        int resBase = pe.RvaToOffset(pe.ResourceRva);
        if (resBase < 0) return guids;

        // Root resource directory: find the entry whose (string) type name is "WEVT_TEMPLATE".
        foreach (var (nameOffsetOrId, dataOrSubdirOffset, isNamed, isSubdir) in ReadResourceEntries(pe, resBase))
        {
            if (!isNamed || !isSubdir) continue;
            var typeName = ReadResourceString(pe, resBase + nameOffsetOrId);
            if (!string.Equals(typeName, "WEVT_TEMPLATE", StringComparison.Ordinal)) continue;
            CollectCrimBlobs(pe, resBase, dataOrSubdirOffset, guids, depth: 0);
        }
        return guids;
    }

    // Walk down the (type → name/id → language) resource subdirectories to each leaf data entry.
    private static void CollectCrimBlobs(PeImage pe, int resBase, int dirOffset, List<Guid> guids, int depth)
    {
        if (depth > 8) return; // resource trees are 3 deep; guard runaway/corrupt offsets
        foreach (var (_, dataOrSubdirOffset, _, isSubdir) in ReadResourceEntries(pe, resBase + dirOffset))
        {
            if (isSubdir)
            {
                CollectCrimBlobs(pe, resBase, dataOrSubdirOffset, guids, depth + 1);
            }
            else
            {
                // IMAGE_RESOURCE_DATA_ENTRY: OffsetToData(RVA,4) | Size(4) | CodePage(4) | Reserved(4)
                int entry = resBase + dataOrSubdirOffset;
                if (!pe.InBounds(entry, 8)) continue;
                uint dataRva = pe.U32(entry);
                uint size = pe.U32(entry + 4);
                int blob = pe.RvaToOffset(dataRva);
                if (blob < 0 || size < 16 || !pe.InBounds(blob, (int)Math.Min(size, int.MaxValue))) continue;
                ParseCrim(pe, blob, (int)size, guids);
            }
        }
    }

    // CRIM header: "CRIM"(4) | size(4) | major(2) | minor(2) | providerCount(4) | then count*(GUID(16)+offset(4)).
    private static void ParseCrim(PeImage pe, int offset, int size, List<Guid> guids)
    {
        if (!pe.InBounds(offset, 16)) return;
        if (pe.Data[offset] != (byte)'C' || pe.Data[offset + 1] != (byte)'R'
            || pe.Data[offset + 2] != (byte)'I' || pe.Data[offset + 3] != (byte)'M') return;
        uint count = pe.U32(offset + 12);
        if (count == 0 || count > 100_000) return;
        int p = offset + 16;
        for (uint i = 0; i < count; i++, p += 20)
        {
            if (!pe.InBounds(p, 16)) break;
            var g = new Guid(new ReadOnlySpan<byte>(pe.Data, p, 16));
            if (g != Guid.Empty) guids.Add(g);
        }
    }

    // Enumerate one IMAGE_RESOURCE_DIRECTORY's entries: (nameOrId, dataOrSubdirOffset, isNamed, isSubdir).
    private static IEnumerable<(int nameOrId, int dataOrSubdir, bool isNamed, bool isSubdir)> ReadResourceEntries(
        PeImage pe, int dirOffset)
    {
        var list = new List<(int, int, bool, bool)>();
        if (!pe.InBounds(dirOffset, 16)) return list;
        int named = pe.U16(dirOffset + 12);
        int ids = pe.U16(dirOffset + 14);
        int total = named + ids;
        int e = dirOffset + 16;
        for (int i = 0; i < total; i++, e += 8)
        {
            if (!pe.InBounds(e, 8)) break;
            uint name = pe.U32(e);
            uint off = pe.U32(e + 4);
            bool isNamed = (name & 0x80000000) != 0;
            bool isSubdir = (off & 0x80000000) != 0;
            list.Add(((int)(name & 0x7FFFFFFF), (int)(off & 0x7FFFFFFF), isNamed, isSubdir));
        }
        return list;
    }

    // IMAGE_RESOURCE_DIR_STRING_U: uint16 length (chars) + UTF-16LE.
    private static string ReadResourceString(PeImage pe, int offset)
    {
        if (!pe.InBounds(offset, 2)) return string.Empty;
        int len = pe.U16(offset);
        if (len <= 0 || len > 512 || !pe.InBounds(offset + 2, len * 2)) return string.Empty;
        return Encoding.Unicode.GetString(pe.Data, offset + 2, len * 2);
    }

    private static string? ResolvePublisherName(Guid g)
    {
        // Best-effort: the friendly name lives in the system publisher registry for registered providers.
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\WINEVT\Publishers\{{{g}}}");
            var v = key?.GetValue(null) as string;
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch { return null; }
    }

    // ---- TraceLogging / self-describing provider metadata ---------------------------------------

    // Provider metadata blob: uint16 totalSize (incl. itself) | UTF-8 provider name + NUL | optional traits.
    private static IEnumerable<string> ScanTraceLoggingProviderNames(PeImage pe)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sec in pe.Sections)
        {
            if (sec.Name != ".rdata" && sec.Name != ".data") continue;
            int start = sec.RawPtr, end = Math.Min(pe.Data.Length, sec.RawPtr + sec.RawSize);
            for (int i = start; i + 2 < end; i++)
            {
                int size = pe.Data[i] | (pe.Data[i + 1] << 8);
                if (size < 4 || size > 4096 || i + size > end) continue;

                // Read the NUL-terminated UTF-8 name right after the size field.
                int n = i + 2, nameEnd = n;
                while (nameEnd < end && pe.Data[nameEnd] != 0
                       && pe.Data[nameEnd] >= 0x20 && pe.Data[nameEnd] < 0x7F) nameEnd++;
                if (nameEnd >= end || pe.Data[nameEnd] != 0) continue;
                int nameLen = nameEnd - n;
                if (nameLen < 3 || nameLen > 256) continue;

                // The size field must account for EXACTLY the size word + name + NUL (+ optional, well-formed
                // provider traits). This is the structural check that separates a real provider-metadata blob
                // from a PE import hint/name table entry (WORD hint + NUL-terminated function name), which has
                // the SAME shape — without it, imported APIs like GetDiskFreeSpaceExW were reported as bogus
                // providers. An import hint is an arbitrary ordinal that won't equal nameLen+3.
                int metaUsed = 2 + nameLen + 1;
                if (size < metaUsed) continue;
                if (size != metaUsed && !ValidProviderTraits(pe, i + metaUsed, i + size)) continue;

                var name = Encoding.ASCII.GetString(pe.Data, n, nameLen);
                if (!LooksLikeProviderName(name)) continue;
                names.Add(name);
            }
        }
        return names;
    }

    // Provider traits tail: zero or more { UINT16 byteCount (incl. itself); BYTE type; BYTE value[...] }
    // structures that must tile the region [start,end) exactly.
    private static bool ValidProviderTraits(PeImage pe, int start, int end)
    {
        int p = start;
        while (p < end)
        {
            if (p + 2 > end) return false;
            int ts = pe.Data[p] | (pe.Data[p + 1] << 8);
            if (ts < 3 || p + ts > end) return false;
            p += ts;
        }
        return p == end;
    }

    private static bool LooksLikeProviderName(string s)
    {
        if (s.Length < 3) return false;
        if (!(char.IsLetter(s[0]))) return false;
        bool hasSep = false;
        foreach (var c in s)
        {
            if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == ' ')) return false;
            if (c == '.' || c == '-') hasSep = true;
        }
        // Real provider names are namespaced (Microsoft.Windows.Foo / MyCompany-Component) or are long
        // mixed identifiers; the separator requirement keeps random short ASCII runs out of the list.
        return hasSep || s.Length >= 6;
    }

    /// <summary>The standard ETW name→GUID hash (EventSource / TraceLogging): SHA-1 over a fixed namespace
    /// GUID plus the upper-cased provider name as UTF-16 big-endian, with the version nibble set to 5.</summary>
    public static Guid GuidFromProviderName(string name)
    {
        byte[] ns = { 0x48, 0x2C, 0x2D, 0xB2, 0xC3, 0x90, 0x47, 0xC8, 0x87, 0xF8, 0x1A, 0x15, 0xBF, 0xC1, 0x30, 0xFB };
        byte[] nameBytes = Encoding.BigEndianUnicode.GetBytes(name.ToUpperInvariant());
        byte[] input = new byte[ns.Length + nameBytes.Length];
        Buffer.BlockCopy(ns, 0, input, 0, ns.Length);
        Buffer.BlockCopy(nameBytes, 0, input, ns.Length, nameBytes.Length);
        byte[] hash;
        using (var sha1 = SHA1.Create()) hash = sha1.ComputeHash(input);
        var g = new byte[16];
        Array.Copy(hash, g, 16);
        g[7] = (byte)((g[7] & 0x0F) | 0x50); // version 5
        return new Guid(g);
    }

    private static bool ContainsBytes(byte[] haystack, byte[] needle)
        => haystack.AsSpan().IndexOf(needle) >= 0;

    // ---- Legacy heuristic (fallback only) -------------------------------------------------------

    private static IEnumerable<(Guid guid, string? name)> HeuristicScan(PeImage pe)
    {
        var results = new List<(Guid, string?)>();
        var scan = pe.Sections.Where(s => s.Name == ".data" || s.Name == ".rdata").ToList();
        if (scan.Count == 0) return results;
        int pointerSize = pe.IsPE32Plus ? 8 : 4;
        var seen = new HashSet<Guid>();
        foreach (var sec in scan)
        {
            int start = sec.RawPtr, secEnd = Math.Min(pe.Data.Length, sec.RawPtr + sec.RawSize);
            for (int i = start; i < secEnd - 16 - pointerSize; i++)
            {
                Guid guid;
                try { guid = new Guid(new ReadOnlySpan<byte>(pe.Data, i, 16)); }
                catch { continue; }
                if (guid == Guid.Empty || seen.Contains(guid)) continue;
                long ptr = pointerSize == 8 ? BitConverter.ToInt64(pe.Data, i + 16) : BitConverter.ToUInt32(pe.Data, i + 16);
                foreach (var tsec in scan)
                {
                    int strOff = pe.RvaToOffset((uint)ptr);
                    if (strOff < tsec.RawPtr || strOff >= tsec.RawPtr + tsec.RawSize) continue;
                    string? candidate = ReadShortAsciiOrUtf16(pe.Data, strOff, Math.Min(pe.Data.Length, tsec.RawPtr + tsec.RawSize));
                    if (candidate != null && seen.Add(guid)) { results.Add((guid, candidate)); break; }
                }
            }
        }
        return results;
    }

    private static string? ReadShortAsciiOrUtf16(byte[] data, int off, int end)
    {
        int len = 0;
        while (off + len < end && data[off + len] >= 0x20 && data[off + len] < 0x7F && len < 64) len++;
        if (len >= 4) return Encoding.ASCII.GetString(data, off, len);
        len = 0;
        while (off + len * 2 + 1 < end && data[off + len * 2] >= 0x20 && data[off + len * 2] < 0x7F
               && data[off + len * 2 + 1] == 0 && len < 64) len++;
        return len >= 4 ? Encoding.Unicode.GetString(data, off, len * 2) : null;
    }

    // ---- Minimal PE reader ----------------------------------------------------------------------

    private sealed class PeImage
    {
        public byte[] Data = Array.Empty<byte>();
        public bool IsPE32Plus;
        public uint ResourceRva;
        public List<Section> Sections = new();

        public readonly record struct Section(string Name, uint Va, uint VirtualSize, int RawPtr, int RawSize);

        public bool InBounds(int offset, int len) => offset >= 0 && len >= 0 && (long)offset + len <= Data.Length;
        public uint U32(int o) => BitConverter.ToUInt32(Data, o);
        public int U16(int o) => BitConverter.ToUInt16(Data, o);

        public int RvaToOffset(uint rva)
        {
            foreach (var s in Sections)
            {
                uint span = Math.Max(s.VirtualSize, (uint)s.RawSize);
                if (rva >= s.Va && rva < s.Va + span)
                {
                    int o = s.RawPtr + (int)(rva - s.Va);
                    return o >= 0 && o < Data.Length ? o : -1;
                }
            }
            return -1;
        }

        public static PeImage Parse(byte[] bytes)
        {
            var pe = new PeImage { Data = bytes };
            if (bytes.Length < 0x40) throw new InvalidDataException("File too small to be a PE");
            int peOff = BitConverter.ToInt32(bytes, 0x3C);
            if (peOff <= 0 || peOff + 24 > bytes.Length || BitConverter.ToUInt32(bytes, peOff) != 0x4550)
                throw new InvalidDataException("Not a valid PE file");

            int coff = peOff + 4;
            int numSections = BitConverter.ToUInt16(bytes, coff + 2);
            int sizeOfOptHeader = BitConverter.ToUInt16(bytes, coff + 16);
            int optStart = coff + 20;
            if (optStart + 2 > bytes.Length) throw new InvalidDataException("Truncated optional header");
            ushort magic = BitConverter.ToUInt16(bytes, optStart);
            pe.IsPE32Plus = magic == 0x20b;

            int dataDirStart = optStart + (pe.IsPE32Plus ? 112 : 96);
            int resEntry = dataDirStart + 2 * 8; // data directory index 2 = Resource Table
            if (resEntry + 8 <= bytes.Length)
                pe.ResourceRva = BitConverter.ToUInt32(bytes, resEntry);

            int secStart = optStart + sizeOfOptHeader;
            for (int i = 0; i < numSections; i++)
            {
                int s = secStart + i * 40;
                if (s + 40 > bytes.Length) break;
                string name = Encoding.UTF8.GetString(bytes, s, 8).TrimEnd('\0');
                uint vsize = BitConverter.ToUInt32(bytes, s + 8);
                uint va = BitConverter.ToUInt32(bytes, s + 12);
                uint rawSize = BitConverter.ToUInt32(bytes, s + 16);
                uint rawPtr = BitConverter.ToUInt32(bytes, s + 20);
                pe.Sections.Add(new Section(name, va, vsize, (int)rawPtr, (int)rawSize));
            }
            return pe;
        }
    }
}
