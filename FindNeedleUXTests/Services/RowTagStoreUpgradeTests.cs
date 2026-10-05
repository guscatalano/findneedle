using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUX.UnitTests.Services;

/// <summary>
/// What happens to tags saved by an OLDER build when the user upgrades.
///
/// RowTagStore stamps every file with <see cref="RowTagStore.FingerprintVersion"/> and ignores any file
/// carrying a different one, deliberately: v1 hashed a different set of row fields, so a v1 fingerprint
/// cannot be matched against a v2 row without risking restoring someone's tag onto the WRONG line.
///
/// These tests pin the consequence rather than approve of it. A v1 file is discarded SILENTLY - the user
/// is not told that the annotations they made before the upgrade are gone, and the file stays on disk
/// looking like it still means something. A v1 record keeps Time, Source and Preview but not the
/// provider/eventId/recordId/pid/tid fields the v2 recipe needs, so the fingerprint genuinely cannot be
/// recomputed; a migration would have to match on time+source+preview instead, which is a product
/// decision (restore more, risk mis-attaching) and not something to do by accident.
/// </summary>
[TestClass]
[DoNotParallelize]
public class RowTagStoreUpgradeTests
{
    private string _dir;
    private static readonly DateTime T = new(2026, 3, 1, 9, 30, 0, DateTimeKind.Utc);
    private const string Log = @"C:\logs\app.etl";

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fn_tagupgrade_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        RowTagStore.SetStorageLocationForTests(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        RowTagStore.ResetStorageForTests();
        try { Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>The v1 on-disk shape, written by hand: one tag file for a source log, Version = 1.</summary>
    private void WriteV1File(string sourceFile, string name, string note, string preview)
    {
        var payload = new
        {
            Version = 1,
            Source = sourceFile,
            Tags = new[]
            {
                new
                {
                    Fingerprint = "v1-recipe-hash-that-no-longer-means-anything",
                    Name = name,
                    Note = note,
                    Time = T.ToString("o"),
                    Source = Path.GetFileName(sourceFile),
                    Preview = preview,
                    TaggedAt = T.ToString("o"),
                }
            },
        };
        // The file NAME is the key and is unchanged between versions, so a v1 file lands exactly where
        // the current build looks for it - which is why it is found and then thrown away.
        File.WriteAllText(RowTagStore.FileFor(sourceFile), JsonSerializer.Serialize(payload));
    }

    [TestMethod]
    public void TagsFromTheOlderVersion_AreDiscarded()
    {
        WriteV1File(Log, "regression", "first bad frame", "[09:30:00] ERROR pipeline stalled");

        var loaded = RowTagStore.Load(new[] { Log }, new[] { Log });

        Assert.AreEqual(0, loaded.Count,
            "a v1 file is ignored - if this ever returns rows, the fingerprints are being trusted across a recipe change");
    }

    [TestMethod]
    public void TheOlderFileIsStillOnDisk_AndNothingSaysItWasIgnored()
    {
        WriteV1File(Log, "regression", "first bad frame", "[09:30:00] ERROR pipeline stalled");
        var path = RowTagStore.FileFor(Log);

        RowTagStore.Load(new[] { Log }, new[] { Log });

        // Documenting the silence: the file is neither migrated, renamed, nor removed, and Load has no
        // way to report that it skipped anything. Anyone adding an upgrade notice starts here.
        Assert.IsTrue(File.Exists(path), "the v1 file is left in place");
        Assert.AreEqual(1, Version(path), "and still says Version 1");
    }

    [TestMethod]
    public void ACurrentVersionFile_StillLoads()
    {
        // The control: the same path, written by the current build, does come back. Without this a
        // discard caused by a broken path would look like correct version handling.
        var tag = new StoredRowTag
        {
            Fingerprint = RowTagStore.Fingerprint(T, "app.etl", "prov", "task", "42", "7", "100", "200", "ERROR pipeline stalled"),
            Name = "regression",
            Note = "first bad frame",
            Time = T.ToString("o"),
            Source = "app.etl",
            Preview = "ERROR pipeline stalled",
        };
        RowTagStore.Set(new[] { Log }, tag);
        RowTagStore.RememberFiles(new[] { Log }, new[] { Log });

        var loaded = RowTagStore.Load(new[] { Log }, new[] { Log });
        Assert.AreEqual(1, loaded.Count, "a current-version file must load, or the discard test proves nothing");
        Assert.AreEqual("regression", loaded[0].Name);
    }

    private static int Version(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("Version").GetInt32();
    }
}
