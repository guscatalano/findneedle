using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace findneedle.ETWPlugin;

public static class EtwNativeProviderScanner
{
    // Extracts ETW provider GUIDs and names from a native EXE/DLL by parsing the PE file and scanning for ETW registration structures
    public static List<(Guid guid, string? name)> ExtractNativeEtwProviders(string path)
    {
        var results = new List<(Guid, string?)>();
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs);

        // --- Parse DOS header ---
        fs.Seek(0x3C, SeekOrigin.Begin);
        int peHeaderOffset = br.ReadInt32();
        fs.Seek(peHeaderOffset, SeekOrigin.Begin);
        if (br.ReadUInt32() != 0x4550) // "PE\0\0"
            throw new InvalidDataException("Not a valid PE file");

        // --- Parse COFF header ---
        br.ReadUInt16(); // Machine
        var numSections = br.ReadUInt16();
        br.ReadUInt32(); // TimeDateStamp
        br.ReadUInt32(); // PointerToSymbolTable
        br.ReadUInt32(); // NumberOfSymbols
        var sizeOfOptionalHeader = br.ReadUInt16();
        br.ReadUInt16(); // Characteristics

        // --- Parse Optional header ---
        var optHeaderStart = fs.Position;
        var magic = br.ReadUInt16();
        var isPE32Plus = magic == 0x20b;
        fs.Seek(optHeaderStart + sizeOfOptionalHeader, SeekOrigin.Begin);

        // --- Parse Section headers ---
        var sectionHeadersStart = optHeaderStart + sizeOfOptionalHeader;
        fs.Seek(sectionHeadersStart, SeekOrigin.Begin);
        var sections = new List<(string name, int va, int ptr, int size)>();
        for (var i = 0; i < numSections; i++)
        {
            fs.Seek(sectionHeadersStart + i * 40, SeekOrigin.Begin);
            var nameBytes = br.ReadBytes(8);
            var sectionName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
            var virtualSize = br.ReadUInt32();
            var virtualAddress = br.ReadUInt32();
            var sizeOfRawData = br.ReadUInt32();
            var pointerToRawData = br.ReadUInt32();
            br.ReadUInt32(); // PointerToRelocations
            br.ReadUInt32(); // PointerToLinenumbers
            br.ReadUInt16(); // NumberOfRelocations
            br.ReadUInt16(); // NumberOfLinenumbers
            br.ReadUInt32(); // Characteristics
            if (sectionName == ".data" || sectionName == ".rdata")
            {
                sections.Add((sectionName, (int)virtualAddress, (int)pointerToRawData, (int)sizeOfRawData));
            }
        }
        if (sections.Count == 0)
            return results; // No .data/.rdata section

        // Read all relevant sections into memory
        var sectionData = new Dictionary<string, (byte[] data, int va)>();
        foreach (var sec in sections)
        {
            fs.Seek(sec.ptr, SeekOrigin.Begin);
            sectionData[sec.name] = (br.ReadBytes(sec.size), sec.va);
        }

        // Scan for GUID+pointer in all sections
        int pointerSize = IntPtr.Size; // 8 for x64, 4 for x86
        foreach (var (sectionName, va, _, _) in sections)
        {
            var data = sectionData[sectionName].data;
            for (int i = 0; i < data.Length - 16 - pointerSize; i++)
            {
                try
                {
                    var guid = new Guid(new ReadOnlySpan<byte>(data, i, 16));
                    long ptr = pointerSize == 8
                        ? BitConverter.ToInt64(data, i + 16)
                        : BitConverter.ToUInt32(data, i + 16);
                    // Try to resolve pointer in all string sections
                    foreach (var (targetName, targetVa, _, _) in sections)
                    {
                        int stringOffset = (int)(ptr - targetVa);
                        var targetData = sectionData[targetName].data;
                        if (stringOffset > 0 && stringOffset < targetData.Length - 4)
                        {
                            // Try ASCII
                            int len = 0;
                            while (stringOffset + len < targetData.Length && targetData[stringOffset + len] >= 0x20 && targetData[stringOffset + len] < 0x7F && len < 64)
                                len++;
                            if (len >= 4)
                            {
                                string candidate = Encoding.ASCII.GetString(targetData, stringOffset, len);
                                if (!results.Exists(x => x.Item1 == guid))
                                    results.Add((guid, candidate));
                                continue;
                            }
                            // Try UTF-16LE
                            len = 0;
                            while (stringOffset + len * 2 + 1 < targetData.Length && targetData[stringOffset + len * 2] >= 0x20 && targetData[stringOffset + len * 2] < 0x7F && targetData[stringOffset + len * 2 + 1] == 0 && len < 64)
                                len++;
                            if (len >= 4)
                            {
                                string candidate = Encoding.Unicode.GetString(targetData, stringOffset, len * 2);
                                if (!results.Exists(x => x.Item1 == guid))
                                    results.Add((guid, candidate));
                            }
                        }
                    }
                }
                catch { }
            }
        }
        return results;
    }
}
