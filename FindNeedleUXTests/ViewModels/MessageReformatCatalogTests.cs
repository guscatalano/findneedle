using System.IO;
using System.Linq;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="MessageReformatCatalog"/> — the generic message-reformat rules that break a dense
/// one-line Message into named fields for display. Uses the storage seam to redirect to a temp file.
/// [DoNotParallelize] because it mutates a static redirected path.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize]
public class MessageReformatCatalogTests
{
    private string _file = null!;

    [TestInitialize]
    public void Setup()
    {
        _file = Path.Combine(Path.GetTempPath(), $"FN_reformat_{System.Guid.NewGuid():N}.json");
        MessageReformatCatalog.SetStorageLocationForTests(_file);
    }

    [TestCleanup]
    public void Cleanup()
    {
        MessageReformatCatalog.ResetStorageForTests();
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    [TestMethod]
    public void Defaults_IncludeDismBuiltIn_EnabledByDefault()
    {
        var all = MessageReformatCatalog.GetAll();
        var dism = all.FirstOrDefault(r => r.Id == "builtin:dism");
        Assert.IsNotNull(dism, "DISM is a shipped built-in example");
        Assert.IsTrue(dism.Enabled, "built-ins ship enabled");
        Assert.IsTrue(dism.BuiltIn);
    }

    [TestMethod]
    public void Apply_DismLine_BreaksIntoNamedFields()
    {
        var msg = "2026-06-20 09:33:36, Info DISM API: PID=24288 TID=16576 DismApi.dll: <----- Starting DismApi.dll session -----> - DismInitializeInternal";
        var r = MessageReformatCatalog.Apply(msg);
        Assert.IsNotNull(r, "the DISM built-in should match a DISM line");
        var fields = r.Fields.ToDictionary(f => f.Field, f => f.Value);
        Assert.AreEqual("2026-06-20 09:33:36", fields["Timestamp"]);
        Assert.AreEqual("Info", fields["Level"]);
        Assert.AreEqual("API", fields["Component"]);
        Assert.AreEqual("24288", fields["PID"]);
        Assert.AreEqual("16576", fields["TID"]);
        Assert.AreEqual("DismInitializeInternal", fields["Function"]);
        StringAssert.Contains(fields["Payload"], "Starting DismApi.dll session");
    }

    [TestMethod]
    public void Apply_DismLine_WithoutComponent_OmitsEmptyField()
    {
        var msg = "2026-06-20 09:33:36, Info DISM PID=24288 TID=15484 Successfully loaded the ImageSession - CDISMManager::LoadLocalImageSession";
        var r = MessageReformatCatalog.Apply(msg);
        Assert.IsNotNull(r);
        var keys = r.Fields.Select(f => f.Field).ToList();
        Assert.IsFalse(keys.Contains("Component"), "absent optional fields are skipped, not shown empty");
        Assert.IsTrue(keys.Contains("PID"));
    }

    [TestMethod]
    public void Apply_NonMatchingLine_ReturnsNull()
    {
        Assert.IsNull(MessageReformatCatalog.Apply("just some random text with no structure"));
    }

    [TestMethod]
    public void DisabledRule_DoesNotApply()
    {
        MessageReformatCatalog.SetEnabled("builtin:dism", false);
        MessageReformatCatalog.SetEnabled("builtin:cbs", false);
        var msg = "2026-06-20 09:33:36, Info DISM API: PID=1 TID=2 hello - Func";
        Assert.IsNull(MessageReformatCatalog.Apply(msg), "disabled rules should not fire");
    }

    [TestMethod]
    public void SetEnabled_BuiltIn_Persists()
    {
        MessageReformatCatalog.SetEnabled("builtin:dism", false);
        Assert.IsFalse(MessageReformatCatalog.GetById("builtin:dism").Enabled);
        MessageReformatCatalog.SetEnabled("builtin:dism", true);
        Assert.IsTrue(MessageReformatCatalog.GetById("builtin:dism").Enabled);
    }

    [TestMethod]
    public void Upsert_AddsUserRule_AndItApplies()
    {
        // Disable built-ins so only the user rule can match.
        MessageReformatCatalog.SetEnabled("builtin:dism", false);
        MessageReformatCatalog.SetEnabled("builtin:cbs", false);
        MessageReformatCatalog.Upsert(new MessageReformatRule
        {
            Name = "MyApp",
            Pattern = @"^\[(?<When>[^\]]+)\]\s+(?<Sev>\w+):\s+(?<Text>.*)$",
        });
        var r = MessageReformatCatalog.Apply("[12:00] ERROR: disk full");
        Assert.IsNotNull(r);
        Assert.AreEqual("MyApp", r.RuleName);
        var fields = r.Fields.ToDictionary(f => f.Field, f => f.Value);
        Assert.AreEqual("12:00", fields["When"]);
        Assert.AreEqual("ERROR", fields["Sev"]);
        Assert.AreEqual("disk full", fields["Text"]);
    }

    [TestMethod]
    public void Remove_UserRule_Works()
    {
        var u = MessageReformatCatalog.Upsert(new MessageReformatRule { Name = "Tmp", Pattern = @"(?<A>.+)" });
        Assert.IsNotNull(MessageReformatCatalog.GetById(u.Id));
        MessageReformatCatalog.Remove(u.Id);
        Assert.IsNull(MessageReformatCatalog.GetById(u.Id));
    }

    [TestMethod]
    public void Upsert_CannotImpersonateBuiltIn()
    {
        var u = MessageReformatCatalog.Upsert(new MessageReformatRule { Id = "builtin:dism", Name = "evil", Pattern = @"(?<A>.+)" });
        Assert.AreNotEqual("builtin:dism", u.Id, "user rules can't reuse a builtin id");
        Assert.IsFalse(u.BuiltIn);
        // The real built-in is untouched.
        Assert.AreEqual("DISM log line", MessageReformatCatalog.GetById("builtin:dism").Name);
    }

    [TestMethod]
    public void TryValidatePattern_RequiresNamedGroup()
    {
        Assert.IsFalse(MessageReformatCatalog.TryValidatePattern(@"no groups here", out _));
        Assert.IsFalse(MessageReformatCatalog.TryValidatePattern(@"(unnamed)", out _));
        Assert.IsTrue(MessageReformatCatalog.TryValidatePattern(@"(?<X>.+)", out _));
    }

    [TestMethod]
    public void TryValidatePattern_RejectsInvalidRegex()
    {
        Assert.IsFalse(MessageReformatCatalog.TryValidatePattern(@"(?<X>(", out var err));
        Assert.IsFalse(string.IsNullOrEmpty(err));
    }

    [TestMethod]
    public void Move_ReordersUserRules()
    {
        var a = MessageReformatCatalog.Upsert(new MessageReformatRule { Name = "AAA", Pattern = @"(?<A>.+)" });
        var b = MessageReformatCatalog.Upsert(new MessageReformatRule { Name = "BBB", Pattern = @"(?<B>.+)" });

        int IndexOf(string id) => MessageReformatCatalog.GetAll().ToList().FindIndex(r => r.Id == id);
        Assert.IsTrue(IndexOf(a.Id) < IndexOf(b.Id), "precondition: A added before B");

        MessageReformatCatalog.Move(b.Id, -1); // move B up past A
        Assert.IsTrue(IndexOf(b.Id) < IndexOf(a.Id), "after Move(-1), B should precede A");
    }
}
