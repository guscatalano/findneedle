using System;
using System.IO;
using System.Text;

namespace FindNeedleUXTests.WppSymbols;

/// <summary>
/// Writes a minimal but structurally valid MSF 7.0 (native) PDB with a chosen GUID + age, so the
/// discovery tests can exercise exact-match verification without needing a compiler or the WDK.
/// Layout: 4 × 512-byte blocks — superblock, block map, stream directory, PDB info stream.
/// </summary>
internal static class TestPdbFactory
{
    public static void WriteMsfPdb(string path, Guid guid, int age)
    {
        const int bs = 512;
        var file = new byte[bs * 4];

        // Block 0: superblock. Magic is "Microsoft C/C++ MSF 7.00" CR LF 0x1A "DS" 0 0 0.
        Encoding.ASCII.GetBytes("Microsoft C/C++ MSF 7.00").CopyTo(file, 0);
        file[24] = 0x0D; file[25] = 0x0A; file[26] = 0x1A; file[27] = (byte)'D'; file[28] = (byte)'S';
        BitConverter.GetBytes(bs).CopyTo(file, 32);   // block size
        BitConverter.GetBytes(2).CopyTo(file, 36);    // free block map (unused by the reader)
        BitConverter.GetBytes(4).CopyTo(file, 40);    // block count
        BitConverter.GetBytes(16).CopyTo(file, 44);   // directory bytes
        BitConverter.GetBytes(0).CopyTo(file, 48);    // reserved
        BitConverter.GetBytes(1).CopyTo(file, 52);    // block map lives in block 1

        // Block 1: block map — the directory occupies block 2.
        BitConverter.GetBytes(2).CopyTo(file, bs * 1);

        // Block 2: stream directory — 2 streams; stream 0 empty, stream 1 (PDB info) 28 bytes in block 3.
        int o = bs * 2;
        BitConverter.GetBytes(2).CopyTo(file, o);       // stream count
        BitConverter.GetBytes(0).CopyTo(file, o + 4);   // stream 0 size
        BitConverter.GetBytes(28).CopyTo(file, o + 8);  // stream 1 size
        BitConverter.GetBytes(3).CopyTo(file, o + 12);  // stream 1's block

        // Block 3: PDB info stream header — Version, Signature, Age, Guid.
        o = bs * 3;
        BitConverter.GetBytes(20000404).CopyTo(file, o);   // VC70 version
        BitConverter.GetBytes(0).CopyTo(file, o + 4);      // signature (timestamp)
        BitConverter.GetBytes(age).CopyTo(file, o + 8);
        guid.ToByteArray().CopyTo(file, o + 12);

        File.WriteAllBytes(path, file);
    }
}
