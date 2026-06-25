using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using findneedle.Implementations;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestProcessorPlugin;

namespace CoreTests;

/// <summary>
/// Tests for <see cref="ArchiveExtractor"/> — the .zip/.cab extraction that lets the folder scan open
/// archive contents. The .cab path is exercised end-to-end against Windows' real makecab/expand.exe.
/// </summary>
[TestClass]
public class ArchiveExtractorTests
{
    private string _work = null!;

    [TestInitialize]
    public void Setup()
    {
        _work = Path.Combine(Path.GetTempPath(), $"arctest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_work);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_work)) Directory.Delete(_work, true); } catch { }
    }

    [TestMethod]
    public void IsArchive_RecognizesZipAndCab_NotOthers()
    {
        Assert.IsTrue(ArchiveExtractor.IsArchive(".zip"));
        Assert.IsTrue(ArchiveExtractor.IsArchive(".cab"));
        Assert.IsTrue(ArchiveExtractor.IsArchive(".CAB"));
        Assert.IsFalse(ArchiveExtractor.IsArchive(".txt"));
        Assert.IsFalse(ArchiveExtractor.IsArchive(""));
        Assert.IsFalse(ArchiveExtractor.IsArchive(null));
    }

    [TestMethod]
    public void TryExtract_Zip_ExtractsContents()
    {
        var zip = Path.Combine(_work, "bundle.zip");
        var entryFile = Path.Combine(_work, "inside.log");
        File.WriteAllText(entryFile, "hello from zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            z.CreateEntryFromFile(entryFile, "inside.log");

        var dest = Path.Combine(_work, "zip_out");
        Assert.IsTrue(ArchiveExtractor.TryExtract(zip, dest));
        Assert.AreEqual("hello from zip", File.ReadAllText(Path.Combine(dest, "inside.log")));
    }

    [TestMethod]
    public void TryExtract_Cab_ExtractsContents()
    {
        var one = Path.Combine(_work, "one.log");
        var two = Path.Combine(_work, "two.txt");
        File.WriteAllText(one, "hello from cab");
        File.WriteAllText(two, "second member");
        var cab = Path.Combine(_work, "bundle.cab");
        Assert.IsTrue(MakeCab(new[] { one, two }, cab), "makecab should have produced a .cab");

        var dest = Path.Combine(_work, "cab_out");
        Assert.IsTrue(ArchiveExtractor.TryExtract(cab, dest), "expand.exe should extract the cab");

        var oneHit = Directory.GetFiles(dest, "one.log", SearchOption.AllDirectories);
        var twoHit = Directory.GetFiles(dest, "two.txt", SearchOption.AllDirectories);
        Assert.IsTrue(oneHit.Length > 0, "expected one.log to be extracted");
        Assert.IsTrue(twoHit.Length > 0, "expected two.txt to be extracted");
        Assert.AreEqual("hello from cab", File.ReadAllText(oneHit[0]));
        Assert.AreEqual("second member", File.ReadAllText(twoHit[0]));
    }

    [TestMethod]
    public void TryExtract_MissingFile_ReturnsFalse_NoThrow()
    {
        Assert.IsFalse(ArchiveExtractor.TryExtract(Path.Combine(_work, "nope.cab"), Path.Combine(_work, "out")));
    }

    // ── End-to-end: opening an archive runs its inner files through the folder-scan pipeline ──

    [TestMethod]
    public void FolderLocation_OpeningZip_SearchesInnerFiles()
    {
        var inner = Path.Combine(_work, "inner.txt");
        File.WriteAllText(inner, "log line");
        var zip = Path.Combine(_work, "logs.zip");
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
            z.CreateEntryFromFile(inner, "inner.txt");

        Assert.AreEqual(2, SearchArchive(zip), "the .txt inside the zip should be parsed (2 rows per file)");
    }

    [TestMethod]
    public void FolderLocation_OpeningCab_SearchesInnerFiles()
    {
        var inner = Path.Combine(_work, "inner.txt");
        File.WriteAllText(inner, "log line");
        var cab = Path.Combine(_work, "logs.cab");
        Assert.IsTrue(MakeCab(new[] { inner }, cab), "makecab should produce a .cab");

        Assert.AreEqual(2, SearchArchive(cab), "the .txt inside the cab should be parsed (2 rows per file)");
    }

    // Run a single archive file through FolderLocation with a .txt processor (yields 2 rows per file),
    // so a non-zero result count proves the archive was extracted and its contents recursed.
    private static int SearchArchive(string archivePath)
    {
        var loc = new FolderLocation { path = archivePath };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new SampleFileExtensionProcessor() });
        loc.LoadInMemory();
        return loc.Search().Count;
    }

    // Build a realistic multi-member cab with Windows' makecab via a directive (.ddf) file — the same
    // FCI format real cabs use, so expand.exe's -F:* applies (a simple single-file makecab does not).
    private static bool MakeCab(string[] sources, string dest)
    {
        var dir = Path.GetDirectoryName(dest)!;
        var ddf = Path.Combine(dir, "make.ddf");
        var lines = new System.Collections.Generic.List<string>
        {
            $".Set CabinetNameTemplate={Path.GetFileName(dest)}",
            $".Set DiskDirectoryTemplate={dir}",
            ".Set Cabinet=on",
            ".Set Compress=on",
        };
        lines.AddRange(sources);
        File.WriteAllLines(ddf, lines);

        var make = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "makecab.exe");
        if (!File.Exists(make)) make = "makecab.exe";
        var psi = new ProcessStartInfo
        {
            FileName = make,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(ddf);
        using var p = Process.Start(psi);
        if (p == null) return false;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode == 0 && File.Exists(dest);
    }
}
