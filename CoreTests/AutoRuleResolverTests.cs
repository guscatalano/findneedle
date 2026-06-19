using System.Collections.Generic;
using FindPluginCore.Searching.AutoRules;

namespace CoreTests;

/// <summary>
/// Tests for <see cref="AutoRuleResolver"/> — the pure matching that decides which auto-add rules
/// apply to a search. Covers each criterion, AND-across-criteria, the never-match-when-empty guard,
/// glob matching, and de-duplication of resolved paths.
/// </summary>
[TestClass]
public sealed class AutoRuleResolverTests
{
    private static AutoRuleContext Ctx(
        string[] paths = null, string[] sources = null, string[] providers = null, int? build = null)
    {
        var c = new AutoRuleContext { Build = build };
        if (paths != null) c.Paths.AddRange(paths);
        if (sources != null) foreach (var s in sources) c.SourceTypes.Add(s);
        if (providers != null) foreach (var p in providers) c.Providers.Add(p);
        return c;
    }

    private static AutoRuleEntry Entry(AutoRuleCondition cond, string path = "r.rules.json", bool enabled = true)
        => new() { Name = "x", RulePath = path, Enabled = enabled, Condition = cond };

    [TestMethod]
    public void Always_Matches_AnyContext()
    {
        Assert.IsTrue(AutoRuleResolver.Matches(new AutoRuleCondition { Always = true }, Ctx()));
    }

    [TestMethod]
    public void EmptyCondition_NeverMatches()
    {
        Assert.IsFalse(AutoRuleResolver.Matches(new AutoRuleCondition(), Ctx(paths: new[] { "a.etl" })));
    }

    [TestMethod]
    public void Extension_MatchesOnSuffix_CaseInsensitive()
    {
        var cond = new AutoRuleCondition { Extensions = { ".etl" } };
        Assert.IsTrue(AutoRuleResolver.Matches(cond, Ctx(paths: new[] { @"C:\logs\Trace.ETL" })));
        Assert.IsFalse(AutoRuleResolver.Matches(cond, Ctx(paths: new[] { @"C:\logs\app.evtx" })));
    }

    [TestMethod]
    public void SourceType_Matches()
    {
        var cond = new AutoRuleCondition { SourceTypes = { AutoRuleSourceKinds.EventLog } };
        Assert.IsTrue(AutoRuleResolver.Matches(cond, Ctx(sources: new[] { AutoRuleSourceKinds.EventLog })));
        Assert.IsFalse(AutoRuleResolver.Matches(cond, Ctx(sources: new[] { AutoRuleSourceKinds.Etw })));
    }

    [TestMethod]
    public void Glob_MatchesAcrossSeparators()
    {
        Assert.IsTrue(AutoRuleResolver.GlobMatches("*panther*", @"C:\Windows\Panther\setup.log"));
        Assert.IsTrue(AutoRuleResolver.GlobMatches("*cbs*.log", @"C:/logs/CBS.log"));
        Assert.IsFalse(AutoRuleResolver.GlobMatches("*cbs*.log", @"C:\logs\app.txt"));
    }

    [TestMethod]
    public void BuildRange_RequiresKnownBuild()
    {
        var cond = new AutoRuleCondition { MinBuild = 22000, MaxBuild = 26999 };
        Assert.IsTrue(AutoRuleResolver.Matches(cond, Ctx(build: 26200)));
        Assert.IsFalse(AutoRuleResolver.Matches(cond, Ctx(build: 19041)));
        Assert.IsFalse(AutoRuleResolver.Matches(cond, Ctx(build: null)), "no build known => can't match a build range");
    }

    [TestMethod]
    public void MultipleCriteria_AreAnded()
    {
        var cond = new AutoRuleCondition { Extensions = { ".etl" }, SourceTypes = { AutoRuleSourceKinds.Etw } };
        Assert.IsTrue(AutoRuleResolver.Matches(cond, Ctx(paths: new[] { "t.etl" }, sources: new[] { AutoRuleSourceKinds.Etw })));
        // extension matches but source kind doesn't -> overall fail (AND)
        Assert.IsFalse(AutoRuleResolver.Matches(cond, Ctx(paths: new[] { "t.etl" }, sources: new[] { AutoRuleSourceKinds.Folder })));
    }

    [TestMethod]
    public void Resolve_SkipsDisabled_AndDeduplicates()
    {
        var entries = new List<AutoRuleEntry>
        {
            Entry(new AutoRuleCondition { Always = true }, "a.rules.json"),
            Entry(new AutoRuleCondition { Always = true }, "a.rules.json"), // dup path
            Entry(new AutoRuleCondition { Always = true }, "b.rules.json", enabled: false), // disabled
            Entry(new AutoRuleCondition { Extensions = { ".etl" } }, "c.rules.json"),
        };
        var paths = AutoRuleResolver.Resolve(entries, Ctx(paths: new[] { "x.evtx" }));
        CollectionAssert.AreEqual(new[] { "a.rules.json" }, paths);
    }
}
