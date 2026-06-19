using System.IO;
using FindPluginCore.Searching.AutoRules;

namespace CoreTests;

/// <summary>Round-trip + behavior tests for <see cref="AutoRulesStore"/> using the test storage seam
/// (so nothing touches the real %LocalAppData% file).</summary>
[TestClass]
public sealed class AutoRulesStoreTests
{
    private string _file = null!;

    [TestInitialize]
    public void Setup()
    {
        _file = Path.Combine(Path.GetTempPath(), $"FN_autorules_{System.Guid.NewGuid():N}.json");
        AutoRulesStore.SetStorageLocationForTests(_file);
    }

    [TestCleanup]
    public void Cleanup()
    {
        AutoRulesStore.ResetStorageForTests();
        try { if (File.Exists(_file)) File.Delete(_file); } catch { }
    }

    [TestMethod]
    public void Upsert_Persists_AndResolveHonorsMasterSwitch()
    {
        // Point an entry at a real file so ResolveForSearch's File.Exists check passes.
        var rulePath = Path.Combine(Path.GetTempPath(), $"FN_rule_{System.Guid.NewGuid():N}.rules.json");
        File.WriteAllText(rulePath, "{}");
        try
        {
            AutoRulesStore.Upsert(new AutoRuleEntry
            {
                Name = "always",
                RulePath = rulePath,
                Enabled = true,
                Condition = new AutoRuleCondition { Always = true },
            });

            // Reload from disk to prove it persisted.
            AutoRulesStore.SetStorageLocationForTests(_file);
            var ctx = new AutoRuleContext();

            Assert.IsTrue(AutoRulesStore.Enabled, "default master switch is on");
            CollectionAssert.Contains(AutoRulesStore.ResolveForSearch(ctx), rulePath);

            // Per-search opt-out and global off both suppress it.
            Assert.AreEqual(0, AutoRulesStore.ResolveForSearch(ctx, skipForThisSearch: true).Count);
            AutoRulesStore.Enabled = false;
            Assert.AreEqual(0, AutoRulesStore.ResolveForSearch(ctx).Count);
        }
        finally { try { File.Delete(rulePath); } catch { } }
    }
}
