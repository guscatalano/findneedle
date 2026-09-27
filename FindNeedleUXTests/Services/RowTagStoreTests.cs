using System;
using System.IO;
using System.Linq;
using FindNeedleUX;
using FindNeedleUX.Services;
using FindNeedlePluginLib;
using FindNeedleUXTests.Mcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUX.UnitTests.Services;

/// <summary>
/// Tags that outlive the session: the row fingerprint (what it ignores, what it doesn't), and the
/// store's filing - per SOURCE FILE, so a tag belongs to the log rather than to whatever else was
/// open when it was made.
/// </summary>
[TestClass]
[DoNotParallelize]
public class RowTagStoreTests
{
    private string _dir;
    private static readonly DateTime T = new(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc);
    private const string LogA = @"C:\logs\app.etl";
    private const string LogB = @"C:\logs\other.etl";

    [TestInitialize]
    public void Init()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"fn_tags_{Guid.NewGuid():N}");
        RowTagStore.SetStorageLocationForTests(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        RowTagStore.ResetStorageForTests();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    private static string Fp(DateTime time, string source = LogA, string provider = "Prov",
                            string task = "Task", string text = "the message", string pid = "100") =>
        RowTagStore.Fingerprint(time, source, provider, task, eventId: "7", recordId: "", processId: pid, threadId: "4", text);

    private static StoredRowTag Tag(string fp, string name = "Cause", string note = "why it matters", string source = LogA) =>
        new() { Fingerprint = fp, Name = name, Note = note, Time = T.ToString("o"), Source = source, Preview = "the message" };

    // ----- the fingerprint -----

    [TestMethod]
    public void SameRowContent_FingerprintsTheSame_AcrossRuns()
        => Assert.AreEqual(Fp(T), Fp(T), "the recipe must be deterministic - it is the identity across rescans");

    [TestMethod]
    public void ADifferentRow_FingerprintsDifferently()
    {
        var baseline = Fp(T);
        Assert.AreNotEqual(baseline, Fp(T.AddTicks(1)), "a different timestamp is a different row");
        Assert.AreNotEqual(baseline, Fp(T, text: "another message"));
        Assert.AreNotEqual(baseline, Fp(T, provider: "Other"));
        Assert.AreNotEqual(baseline, Fp(T, pid: "101"));
    }

    [TestMethod]
    public void TheSameLogInADifferentFolder_KeepsItsTags()
        => Assert.AreEqual(Fp(T, @"C:\logs\app.etl"), Fp(T, @"D:\copy\APP.ETL"),
            "the file name identifies the source, so re-extracting or moving a log does not orphan its tags");

    [TestMethod]
    public void FingerprintOfARow_UsesTheUnmodifiedText()
    {
        var line = new LogLine(new FakeResult("[2026-03-01 09:30:00] INFO: started", level: Level.Info), 0);
        var again = new LogLine(new FakeResult("[2026-03-01 09:30:00] INFO: started", level: Level.Info), 41);
        Assert.AreEqual(RowTagStore.Fingerprint(line), RowTagStore.Fingerprint(again),
            "the row's position in the result set is not part of its identity");
        Assert.AreNotEqual("", RowTagStore.Fingerprint(line));
    }

    // ----- the store -----

    [TestMethod]
    public void ATag_ComesBackAfterTheSessionEnds()
    {
        RowTagStore.Set(new[] { LogA }, Tag(Fp(T)));
        var loaded = RowTagStore.Load(new[] { LogA });
        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual("Cause", loaded[0].Name);
        Assert.AreEqual("why it matters", loaded[0].Note);
        Assert.AreEqual(T, loaded[0].TimeOrDefault);
        Assert.AreNotEqual("", loaded[0].TaggedAt, "the store stamps when the tag was made");
    }

    [TestMethod]
    public void TaggingTheSameRowTwice_ReplacesTheTag()
    {
        var fp = Fp(T);
        RowTagStore.Set(new[] { LogA }, Tag(fp, "Cause", "first"));
        RowTagStore.Set(new[] { LogA }, Tag(fp, "Effect", "second"));
        var loaded = RowTagStore.Load(new[] { LogA });
        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual("Effect", loaded[0].Name);
        Assert.AreEqual("second", loaded[0].Note);
    }

    /// <summary>
    /// The one that used to be wrong: tags were filed under the whole set of loaded locations, so
    /// adding a second log to the workspace changed the key and the first log's tags stopped coming
    /// back. A tag belongs to its log.
    /// </summary>
    [TestMethod]
    public void AddingASecondLog_DoesNotHideTheFirstLogsTags()
    {
        RowTagStore.Set(new[] { LogA }, Tag(Fp(T)));

        var both = new[] { LogA, LogB };
        Assert.AreEqual(1, RowTagStore.Load(both).Count, "the tag is still found once a second log joins the workspace");

        // And a tag made while both are open is filed under the log its row came from.
        RowTagStore.Set(both, Tag(Fp(T.AddMinutes(1), LogB), "Note", "about B", source: LogB));
        Assert.AreEqual(2, RowTagStore.Load(both).Count);
        Assert.AreEqual(1, RowTagStore.Load(new[] { LogA }).Count, "log A alone shows only its own tag");
        Assert.AreEqual("Cause", RowTagStore.Load(new[] { LogA })[0].Name);
        Assert.AreEqual(1, RowTagStore.Load(new[] { LogB }).Count, "log B alone shows only its own tag");
        Assert.AreEqual("Note", RowTagStore.Load(new[] { LogB })[0].Name);
    }

    [TestMethod]
    public void ALogCarriesItsTags_IntoAnotherWorkspace()
    {
        RowTagStore.Set(new[] { LogA, LogB }, Tag(Fp(T)));
        Assert.AreEqual(1, RowTagStore.Load(new[] { LogA, @"C:\logs\third.etl" }).Count,
            "opened next to a different log, the tag is still on its own file");
        Assert.AreEqual(0, RowTagStore.Load(new[] { @"C:\logs\third.etl" }).Count, "and nowhere else");
    }

    /// <summary>
    /// An archive is one location but many logs inside it. The viewer learns the inner file names
    /// once a load settles and tells the store, so a reopen finds their tags without querying again.
    /// </summary>
    [TestMethod]
    public void AnArchivesInnerFiles_AreFoundAgainThroughTheIndex()
    {
        var zip = new[] { @"C:\bundles\capture.zip" };
        var inner = @"C:\temp\extract\capture\inner-service.log";

        RowTagStore.RememberFiles(zip, new[] { inner, @"C:\temp\extract\capture\other.log" });
        RowTagStore.Set(zip, Tag(Fp(T, inner), source: inner));

        // A later session extracts to a different temp folder: same file names, so the tags are found.
        Assert.AreEqual(1, RowTagStore.Load(zip).Count, "the index remembers which files the zip produces");
        Assert.AreEqual(1, RowTagStore.Load(zip, new[] { @"D:\other\extract\inner-service.log" }).Count,
            "or the caller can just name the files it found this time");
    }

    [TestMethod]
    public void RemoveAndClear_ForgetTags()
    {
        var a = Fp(T); var b = Fp(T.AddSeconds(5));
        RowTagStore.Set(new[] { LogA }, Tag(a));
        RowTagStore.Set(new[] { LogA }, Tag(b));
        Assert.IsTrue(RowTagStore.Remove(new[] { LogA }, a));
        Assert.IsFalse(RowTagStore.Remove(new[] { LogA }, a), "already gone");
        Assert.AreEqual(1, RowTagStore.Load(new[] { LogA }).Count);
        RowTagStore.Clear(new[] { LogA });
        Assert.AreEqual(0, RowTagStore.Load(new[] { LogA }).Count);
        Assert.IsFalse(File.Exists(RowTagStore.FileFor(LogA)), "clearing removes the file rather than leaving an empty one");
    }

    [TestMethod]
    public void RemovingATag_OnlyTouchesItsOwnLog()
    {
        var shared = Fp(T);
        RowTagStore.Set(new[] { LogA }, Tag(shared, source: LogA));
        RowTagStore.Set(new[] { LogB }, Tag(shared, "Note", "b's own", source: LogB));

        Assert.IsTrue(RowTagStore.Remove(new[] { LogA, LogB }, shared, LogA), "naming the file removes only that one");
        Assert.AreEqual(0, RowTagStore.Load(new[] { LogA }).Count);
        Assert.AreEqual(1, RowTagStore.Load(new[] { LogB }).Count, "the other log's tag is untouched");
    }

    [TestMethod]
    public void RemovingTheLastTag_RemovesTheFile()
    {
        var fp = Fp(T);
        RowTagStore.Set(new[] { LogA }, Tag(fp));
        RowTagStore.Remove(new[] { LogA }, fp);
        Assert.IsFalse(File.Exists(RowTagStore.FileFor(LogA)));
    }

    [TestMethod]
    public void All_ListsTheLogsThatHaveTags()
    {
        RowTagStore.Set(new[] { LogA }, Tag(Fp(T)));
        RowTagStore.Set(new[] { LogB }, Tag(Fp(T, LogB), "Note", source: LogB));
        var all = RowTagStore.All();
        Assert.AreEqual(2, all.Count);
        CollectionAssert.AreEquivalent(new[] { 1, 1 }, all.Select(a => a.Count).ToArray());
        Assert.IsTrue(all.Any(a => a.Source.EndsWith("app.etl", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void AnUnreadableOrOutdatedFile_ReadsAsNoTags_NeverThrows()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(RowTagStore.FileFor(LogA), "not json at all");
        Assert.AreEqual(0, RowTagStore.Load(new[] { LogA }).Count);

        File.WriteAllText(RowTagStore.FileFor(LogA),
            "{\"Version\":0,\"Source\":\"app.etl\",\"Tags\":[{\"Fingerprint\":\"abc\",\"Name\":\"Cause\"}]}");
        Assert.AreEqual(0, RowTagStore.Load(new[] { LogA }).Count,
            "a file written by an older fingerprint recipe is ignored, not misapplied");
    }

    [TestMethod]
    public void ATagWithNoFingerprint_IsNotStored()
    {
        RowTagStore.Set(new[] { LogA }, new StoredRowTag { Name = "Cause", Source = LogA });
        Assert.AreEqual(0, RowTagStore.Load(new[] { LogA }).Count);
    }

    [TestMethod]
    public void TheSetIndex_IgnoresOrderAndCase()
    {
        RowTagStore.RememberFiles(new[] { LogA, LogB }, new[] { @"C:\extract\inner.log" });
        RowTagStore.Set(new[] { LogA, LogB }, Tag(Fp(T, @"C:\extract\inner.log"), source: @"C:\extract\inner.log"));
        Assert.AreEqual(1, RowTagStore.Load(new[] { LogB, LogA }).Count, "same set, listed the other way round");
        Assert.AreEqual(1, RowTagStore.Load(new[] { @"c:\LOGS\OTHER.ETL", @"C:\logs\app.etl\" }).Count,
            "case and a trailing slash are noise");
    }
}
