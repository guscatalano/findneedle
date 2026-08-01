using System;
using System.IO;
using System.Text;

namespace FindNeedleUX.Services.WppSymbols;

/// <summary>
/// Minimal reader for a native (MSF 7.0) PDB's identity — the GUID + age in the PDB info stream
/// (stream 1). Used to verify a LOOSE candidate PDB actually matches the binary before TMFs are
/// extracted from it; store-resolved PDBs don't need this, their path IS the GUID+age key.
/// Portable (.NET) PDBs and the ancient 2.0 format return null — callers treat those as
/// unverifiable rather than matching.
///
/// Note on ages: this reads the PDB info stream's age. After a plain build it equals the PE's
/// CodeView age; exotic re-link/PDB-surgery flows can skew them, in which case we over-reject —
/// the failure is loud in the Build TMFs log, never a silent mismatch.
/// </summary>
public static class MsfPdbInfo
{
    // MSF 7.0 magic: "Microsoft C/C++ MSF 7.00" CR LF 0x1A "DS" 0 0 0 — 32 bytes. Built in pieces:
    // a literal "\x1ADS" in source would parse as the single escape \x1AD followed by 'S'.
    private static readonly byte[] Magic = BuildMagic();

    private static byte[] BuildMagic()
    {
        var m = new byte[32];
        Encoding.ASCII.GetBytes("Microsoft C/C++ MSF 7.00").CopyTo(m, 0);
        m[24] = 0x0D; // \r
        m[25] = 0x0A; // \n
        m[26] = 0x1A;
        m[27] = (byte)'D';
        m[28] = (byte)'S';
        // m[29..31] stay 0.
        return m;
    }

    public static (Guid guid, int age)? TryRead(string pdbPath, out string error)
    {
        error = null;
        try
        {
            using var fs = File.OpenRead(pdbPath);
            using var br = new BinaryReader(fs);

            var magic = br.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length || !BytesEqual(magic, Magic))
            {
                error = "not an MSF 7.0 (native) PDB";
                return null;
            }

            uint blockSize = br.ReadUInt32();
            br.ReadUInt32(); // free block map index
            uint numBlocks = br.ReadUInt32();
            uint dirBytes = br.ReadUInt32();
            br.ReadUInt32(); // reserved
            uint blockMapAddr = br.ReadUInt32();
            if (blockSize < 512 || blockSize > (1 << 16) || blockMapAddr == 0 || blockMapAddr >= numBlocks)
            {
                error = "corrupt MSF superblock";
                return null;
            }

            // The block map lists the blocks that hold the stream directory.
            uint dirBlockCount = BlockCount(dirBytes, blockSize);
            if (dirBlockCount == 0) { error = "empty MSF directory"; return null; }
            fs.Position = (long)blockMapAddr * blockSize;
            var dirBlocks = new uint[dirBlockCount];
            for (int i = 0; i < dirBlockCount; i++) dirBlocks[i] = br.ReadUInt32();

            // Directory layout: numStreams, sizes[numStreams], then each stream's block list.
            var dir = ReadBlocks(fs, dirBlocks, blockSize, dirBytes);
            using var dr = new BinaryReader(new MemoryStream(dir));
            uint numStreams = dr.ReadUInt32();
            if (numStreams < 2 || numStreams > 100_000) { error = "PDB has no info stream"; return null; }
            var sizes = new uint[numStreams];
            for (int i = 0; i < numStreams; i++) sizes[i] = dr.ReadUInt32();

            // Skip stream 0's block list to reach stream 1's.
            uint s0Blocks = BlockCount(sizes[0], blockSize);
            for (int i = 0; i < s0Blocks; i++) dr.ReadUInt32();

            uint s1Size = sizes[1] == uint.MaxValue ? 0 : sizes[1];
            if (s1Size < 28) { error = "PDB info stream too small"; return null; }
            // The 28-byte header (Version, Signature, Age, Guid) always fits in the first block.
            uint firstBlock = dr.ReadUInt32();
            if (firstBlock == 0 || firstBlock >= numBlocks) { error = "corrupt PDB info stream"; return null; }
            fs.Position = (long)firstBlock * blockSize;
            var header = br.ReadBytes(28);
            if (header.Length < 28) { error = "truncated PDB info stream"; return null; }

            int age = BitConverter.ToInt32(header, 8);
            var guid = new Guid(header.AsSpan(12, 16));
            return (guid, age);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return null;
        }
    }

    /// <summary>Blocks needed for <paramref name="bytes"/>; a nil stream (0xFFFFFFFF) occupies none.</summary>
    private static uint BlockCount(uint bytes, uint blockSize)
        => bytes == uint.MaxValue ? 0 : (bytes + blockSize - 1) / blockSize;

    private static byte[] ReadBlocks(FileStream fs, uint[] blocks, uint blockSize, uint totalBytes)
    {
        var result = new byte[totalBytes];
        int written = 0;
        foreach (var block in blocks)
        {
            int take = (int)Math.Min(blockSize, totalBytes - (uint)written);
            if (take <= 0) break;
            fs.Position = (long)block * blockSize;
            int got = fs.Read(result, written, take);
            written += got;
            if (got < take) break;
        }
        return result;
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
