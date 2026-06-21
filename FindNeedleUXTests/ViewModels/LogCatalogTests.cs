using System.IO;
using System.Linq;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="LogCatalog"/> — the Log Finder catalog (built-ins + user entries). Uses the
/// storage seam to redirect to a temp file. [DoNotParallelize] because it mutates a static path.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize]
public class LogCatalogTests
{
    private string _file = null!;

    [TestInitialize]
    public void Setup()
    {
        _file = Path.Combine(Path.GetTempPath(), $"FN_logcat_{System.Guid.NewGuid():N}.json");
        LogCatalog.SetStorageLocationForTests(_file);
    }

    [TestCleanup]
    public void Cleanup()
    {
        LogCatalog.ResetStorageForTests();
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    [TestMethod]
    public void GetAll_IncludesBuiltIns_WhenEmpty()
    {
        var all = LogCatalog.GetAll();
        Assert.IsTrue(all.Count >= LogCatalog.BuiltIns.Count);
        Assert.IsTrue(all.All(e => LogCatalog.BuiltIns.Any(b => b.Id == e.Id)), "with no user entries, all entries are built-ins");
        Assert.IsTrue(all.Any(e => e.Id == "builtin:winevt"));
    }

    [TestMethod]
    public void Upsert_AddsUserEntry_AfterBuiltIns()
    {
        var e = LogCatalog.Upsert(new LogCatalogEntry { Name = "MyApp", Path = @"%TEMP%\MyApp", Kind = "folder" });
        Assert.IsFalse(e.BuiltIn);
        Assert.IsFalse(string.IsNullOrEmpty(e.Id));

        var all = LogCatalog.GetAll();
        Assert.AreEqual(LogCatalog.BuiltIns.Count + 1, all.Count);
        Assert.AreEqual("MyApp", all.Last().Name);
    }

    [TestMethod]
    public void Upsert_Update_NoDuplicate()
    {
        var e = LogCatalog.Upsert(new LogCatalogEntry { Name = "A", Path = "p", Kind = "file" });
        e.Name = "B";
        LogCatalog.Upsert(e);
        var users = LogCatalog.GetAll().Where(x => !x.BuiltIn).ToList();
        Assert.AreEqual(1, users.Count);
        Assert.AreEqual("B", users[0].Name);
    }

    [TestMethod]
    public void Upsert_CannotCreateBuiltIn()
    {
        var e = LogCatalog.Upsert(new LogCatalogEntry { Id = "builtin:winevt", Name = "hijack", Path = "x", BuiltIn = true });
        Assert.IsFalse(e.BuiltIn, "user upserts are always non-built-in");
        Assert.AreNotEqual("builtin:winevt", e.Id, "must not overwrite a built-in id");
    }

    [TestMethod]
    public void Remove_DropsUserEntry()
    {
        var e = LogCatalog.Upsert(new LogCatalogEntry { Name = "X", Path = "p" });
        LogCatalog.Remove(e.Id);
        Assert.IsFalse(LogCatalog.GetAll().Any(x => x.Id == e.Id));
    }

    [TestMethod]
    public void Remove_IgnoresBuiltInIds()
    {
        var before = LogCatalog.GetAll().Count;
        LogCatalog.Remove("builtin:winevt");
        Assert.AreEqual(before, LogCatalog.GetAll().Count, "built-ins can't be removed");
    }

    [TestMethod]
    public void UserEntries_Persist_AcrossReload()
    {
        LogCatalog.Upsert(new LogCatalogEntry { Name = "Persisted", Path = @"%TEMP%", Kind = "folder" });
        LogCatalog.SetStorageLocationForTests(_file); // re-point at same file
        Assert.IsTrue(LogCatalog.GetAll().Any(e => e.Name == "Persisted"));
    }

    [TestMethod]
    public void Entry_ExpandsEnvironmentVariables()
    {
        var e = new LogCatalogEntry { Path = @"%TEMP%\sub", Kind = "folder" };
        Assert.IsFalse(e.ExpandedPath.Contains("%TEMP%"), "env vars should be expanded");
    }

    [TestMethod]
    public void BuiltIns_AreValid()
    {
        Assert.IsTrue(LogCatalog.BuiltIns.All(b => b.BuiltIn));
        Assert.IsTrue(LogCatalog.BuiltIns.All(b => !string.IsNullOrWhiteSpace(b.Name) && !string.IsNullOrWhiteSpace(b.Path)));
        var ids = LogCatalog.BuiltIns.Select(b => b.Id).ToList();
        Assert.AreEqual(ids.Count, ids.Distinct().Count(), "built-in ids unique");
    }

    /// <summary>Validate every built-in entry individually: flagged built-in, "builtin:" id, a name +
    /// path, a known kind, and a path whose environment variables resolve to a rooted absolute path.
    /// (Existence isn't asserted — not every machine has every component installed.)</summary>
    [TestMethod]
    [DynamicData(nameof(BuiltInEntries), DynamicDataSourceType.Method,
        DynamicDataDisplayName = nameof(BuiltInDisplayName))]
    public void BuiltIn_Entry_IsWellFormed(LogCatalogEntry b)
    {
        Assert.IsTrue(b.BuiltIn, "built-in entries must be flagged BuiltIn");
        StringAssert.StartsWith(b.Id, "builtin:");
        Assert.IsFalse(string.IsNullOrWhiteSpace(b.Name), "name required");
        Assert.IsFalse(string.IsNullOrWhiteSpace(b.Path), "path required");
        Assert.IsTrue(b.Kind is "folder" or "file", $"unexpected kind '{b.Kind}'");
        Assert.AreEqual(b.Kind == "folder", b.IsFolder);

        var expanded = b.ExpandedPath;
        Assert.IsFalse(expanded.Contains('%'), $"env vars should resolve: {b.Path} -> {expanded}");
        Assert.IsTrue(System.IO.Path.IsPathRooted(expanded), $"resolved path should be absolute: {expanded}");
    }

    public static System.Collections.Generic.IEnumerable<object[]> BuiltInEntries()
        => LogCatalog.BuiltIns.Select(b => new object[] { b });

    public static string BuiltInDisplayName(System.Reflection.MethodInfo _, object[] data)
        => $"Built-in — {((LogCatalogEntry)data[0]).Name}";

    /// <summary>Every built-in's env vars are actually defined on this machine (so the path doesn't
    /// resolve to a bogus literal). Existence of the folder itself is not required.</summary>
    [TestMethod]
    public void BuiltIns_EnvironmentVariables_AreDefined()
    {
        foreach (var b in LogCatalog.BuiltIns)
        {
            // Pull each %VAR% token out of the raw path and confirm it expands to something non-empty.
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(b.Path, "%([^%]+)%"))
            {
                var name = m.Groups[1].Value;
                var val = System.Environment.GetEnvironmentVariable(name);
                Assert.IsFalse(string.IsNullOrEmpty(val), $"{b.Name}: environment variable %{name}% is not set");
            }
        }
    }
}
