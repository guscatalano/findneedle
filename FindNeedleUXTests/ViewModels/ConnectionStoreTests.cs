using System.IO;
using System.Linq;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="ConnectionStore"/> — saved online-source connections. Uses the storage seam to
/// redirect persistence to a temp file. [DoNotParallelize] because it mutates a static redirected path.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
[DoNotParallelize]
public class ConnectionStoreTests
{
    private string _file = null!;

    [TestInitialize]
    public void Setup()
    {
        _file = Path.Combine(Path.GetTempPath(), $"FN_connections_{System.Guid.NewGuid():N}.json");
        ConnectionStore.SetStorageLocationForTests(_file);
    }

    [TestCleanup]
    public void Cleanup()
    {
        ConnectionStore.ResetStorageForTests();
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    [TestMethod]
    public void GetAll_Empty_WhenNothingSaved()
    {
        Assert.AreEqual(0, ConnectionStore.GetAll().Count);
    }

    [TestMethod]
    public void Upsert_AssignsId_AndPersists()
    {
        var c = ConnectionStore.Upsert(new SavedConnection { Kind = "ado", AdoOrg = "https://dev.azure.com/org", AdoProject = "Proj" });
        Assert.IsFalse(string.IsNullOrEmpty(c.Id));

        // Re-point to the same file to prove it persisted.
        ConnectionStore.SetStorageLocationForTests(_file);
        var loaded = ConnectionStore.GetById(c.Id);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("Proj", loaded.AdoProject);
    }

    [TestMethod]
    public void Upsert_DefaultsName_FromFields()
    {
        var c = ConnectionStore.Upsert(new SavedConnection { Kind = "github", GithubRepo = "guscatalano/findneedle" });
        Assert.AreEqual("guscatalano/findneedle", c.Name);
    }

    [TestMethod]
    public void Upsert_Update_ReplacesInPlace_NoDuplicate()
    {
        var c = ConnectionStore.Upsert(new SavedConnection { Kind = "ado", AdoOrg = "o", AdoProject = "p1" });
        c.AdoProject = "p2";
        ConnectionStore.Upsert(c);

        var all = ConnectionStore.GetAll("ado");
        Assert.AreEqual(1, all.Count, "updating by id should not create a duplicate");
        Assert.AreEqual("p2", all[0].AdoProject);
    }

    [TestMethod]
    public void GetAll_FiltersByKind()
    {
        ConnectionStore.Upsert(new SavedConnection { Kind = "ado", AdoOrg = "o", AdoProject = "p" });
        ConnectionStore.Upsert(new SavedConnection { Kind = "github", GithubRepo = "a/b" });
        ConnectionStore.Upsert(new SavedConnection { Kind = "kusto", KustoCluster = "c", KustoDatabase = "d" });

        Assert.AreEqual(1, ConnectionStore.GetAll("ado").Count);
        Assert.AreEqual(1, ConnectionStore.GetAll("github").Count);
        Assert.AreEqual(1, ConnectionStore.GetAll("kusto").Count);
        Assert.AreEqual(3, ConnectionStore.GetAll().Count);
    }

    [TestMethod]
    public void Remove_DropsConnection()
    {
        var c = ConnectionStore.Upsert(new SavedConnection { Kind = "kusto", KustoCluster = "c", KustoDatabase = "d" });
        ConnectionStore.Remove(c.Id);
        Assert.IsNull(ConnectionStore.GetById(c.Id));
        Assert.AreEqual(0, ConnectionStore.GetAll().Count);
    }

    [TestMethod]
    public void Secret_RoundTripsThroughDpapi()
    {
        var c = new SavedConnection { Kind = "ado", AdoOrg = "o", AdoProject = "p" };
        c.AdoPat = "super-secret-token";
        ConnectionStore.Upsert(c);

        ConnectionStore.SetStorageLocationForTests(_file);
        var loaded = ConnectionStore.GetById(c.Id);
        Assert.AreEqual("super-secret-token", loaded.AdoPat, "PAT should round-trip via DPAPI");
        Assert.AreNotEqual("super-secret-token", loaded.AdoPatEnc, "stored value must be encrypted, not plaintext");
    }

    [TestMethod]
    public void DefaultName_PerKind()
    {
        Assert.AreEqual("dev.azure.com/org/Proj",
            new SavedConnection { Kind = "ado", AdoOrg = "https://dev.azure.com/org", AdoProject = "Proj" }.DefaultName());
        Assert.AreEqual("owner/repo",
            new SavedConnection { Kind = "github", GithubRepo = "owner/repo" }.DefaultName());
    }
}
