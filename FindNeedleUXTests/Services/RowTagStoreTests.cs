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
/// Tags that outlive the session: the row fingerprint (what it ignores, what it doesn't) and the
/// per-source-set store.
/// </summary>
[TestClass]
[DoNotParallelize]
public class RowTagStoreTests
{
    private string _dir;
    private static readonly DateTime T = new(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc);
    private static readonly string[] OneLog = { @"C:\logs\app.etl" };

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

    private static string Fp(DateTime time, string source = @"C:\logs\app.etl", string provider = "Prov",
                            string task = "Task", string text = "the message", string pid = "100") =>
        RowTagStore.Fingerprint(time, source, provider, task, eventId: "7", recordId: "", processId: pid, threadId: "4", text);

    private static StoredRowTag Tag(string fp, string name = "Cause", string note = "why it matters") =>
        new() { Fingerprint = fp, Name = name, Note = note, Time = T.ToString("o"), Source = OneLog[0], Preview = "the message" };

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
        // FakeResult gives SearchableData == the message; the LogLine's displayed Message may be
        // cleaned, so the fingerprint must come out the same either way.
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
        var fp = Fp(T);
        RowTagStore.Set(OneLog, Tag(fp));
        var loaded = RowTagStore.Load(OneLog);
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
        RowTagStore.Set(OneLog, Tag(fp, "Cause", "first"));
        RowTagStore.Set(OneLog, Tag(fp, "Effect", "second"));
        var loaded = RowTagStore.Load(OneLog);
        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual("Effect", loaded[0].Name);
        Assert.AreEqual("second", loaded[0].Note);
    }

    [TestMethod]
    public void TagsAreScopedToTheSourceSet_AndTheOrderOfSourcesDoesNotMatter()
    {
        RowTagStore.Set(OneLog, Tag(Fp(T)));
        Assert.AreEqual(0, RowTagStore.Load(new[] { @"C:\logs\other.etl" }).Count, "another log has its own tags");

        var two = new[] { @"C:\logs\app.etl", @"C:\logs\other.etl" };
        RowTagStore.Set(two, Tag(Fp(T.AddMinutes(1)), "Note"));
        Assert.AreEqual(1, RowTagStore.Load(two).Count);
        Assert.AreEqual(1, RowTagStore.Load(two.Reverse().ToArray()).Count, "same set, listed the other way round");
        Assert.AreEqual(1, RowTagStore.Load(new[] { @"c:\LOGS\OTHER.etl", @"C:\logs\app.etl\" }).Count, "case and a trailing slash are noise");
        Assert.AreEqual(1, RowTagStore.Load(OneLog).Count, "the one-log set is untouched by the two-log set");
    }

    [TestMethod]
    public void RemoveAndClear_ForgetTags()
    {
        var a = Fp(T); var b = Fp(T.AddSeconds(5));
        RowTagStore.Set(OneLog, Tag(a));
        RowTagStore.Set(OneLog, Tag(b));
        Assert.IsTrue(RowTagStore.Remove(OneLog, a));
        Assert.IsFalse(RowTagStore.Remove(OneLog, a), "already gone");
        Assert.AreEqual(1, RowTagStore.Load(OneLog).Count);
        RowTagStore.Clear(OneLog);
        Assert.AreEqual(0, RowTagStore.Load(OneLog).Count);
        Assert.IsFalse(File.Exists(RowTagStore.FileFor(OneLog)), "clearing removes the file rather than leaving an empty one");
    }

    [TestMethod]
    public void RemovingTheLastTag_RemovesTheFile()
    {
        var fp = Fp(T);
        RowTagStore.Set(OneLog, Tag(fp));
        RowTagStore.Remove(OneLog, fp);
        Assert.IsFalse(File.Exists(RowTagStore.FileFor(OneLog)));
    }

    [TestMethod]
    public void All_ListsTheSourceSetsThatHaveTags()
    {
        RowTagStore.Set(OneLog, Tag(Fp(T)));
        RowTagStore.Set(new[] { @"C:\logs\other.etl" }, Tag(Fp(T), "Note"));
        var all = RowTagStore.All();
        Assert.AreEqual(2, all.Count);
        CollectionAssert.AreEquivalent(new[] { 1, 1 }, all.Select(a => a.Count).ToArray());
        Assert.IsTrue(all.Any(a => a.Sources.Any(s => s.EndsWith("app.etl", StringComparison.OrdinalIgnoreCase))));
    }

    [TestMethod]
    public void AnUnreadableOrOutdatedFile_ReadsAsNoTags_NeverThrows()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(RowTagStore.FileFor(OneLog), "not json at all");
        Assert.AreEqual(0, RowTagStore.Load(OneLog).Count);

        File.WriteAllText(RowTagStore.FileFor(OneLog),
            "{\"Version\":0,\"Sources\":[],\"Tags\":[{\"Fingerprint\":\"abc\",\"Name\":\"Cause\"}]}");
        Assert.AreEqual(0, RowTagStore.Load(OneLog).Count, "a file written by an older fingerprint recipe is ignored, not misapplied");
    }

    [TestMethod]
    public void ATagWithNoFingerprint_IsNotStored()
    {
        RowTagStore.Set(OneLog, new StoredRowTag { Name = "Cause" });
        Assert.AreEqual(0, RowTagStore.Load(OneLog).Count);
    }
}
