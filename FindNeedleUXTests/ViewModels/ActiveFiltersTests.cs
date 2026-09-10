using System;
using System.Collections.Generic;
using System.Linq;
using FindNeedleUX.Pages.NativeResultViewer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// The filter pane shows every constraint narrowing the view as a removable pill, and the Filters
/// badge shows how many there are. Those used to be two separate hand-kept tallies; they now both come
/// from <see cref="ActiveFilterCatalog.Build"/>, so these tests pin the list's contents, its labels,
/// and the invariant that the badge count IS the list's length.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class ActiveFiltersTests
{
    private static IReadOnlyList<ActiveFilter> Build(ActiveFilterState s) => ActiveFilterCatalog.Build(s);

    private static List<string> Labels(ActiveFilterState s) => Build(s).Select(f => f.Label).ToList();

    [TestMethod]
    public void NothingActive_IsAnEmptyList()
    {
        Assert.AreEqual(0, Build(new ActiveFilterState()).Count);
        Assert.AreEqual(0, ActiveFilterCatalog.Build(null).Count);
    }

    [TestMethod]
    public void BadgeCount_IsTheListLength()
    {
        // The whole point of the refactor: one list, one number. Every constraint contributes exactly
        // one entry, so a caller setting BadgeCount = list.Count cannot disagree with the pills.
        var state = new ActiveFilterState
        {
            Search = "cache-miss",
            Provider = "Kernel",
            TaskName = "PowerTransition",
            Message = "veto",
            Source = "trace.etl",
            Level = "Error",
            LevelSet = new[] { "Error", "Warning" },
            ProviderSet = new[] { "Kernel-Power" },
            From = new DateTime(2026, 8, 4, 9, 0, 0),
            To = new DateTime(2026, 8, 4, 10, 0, 0),
            RuleFilterActive = true,
            RuleFileCount = 1,
        };
        var list = Build(state);
        // search + from + to + level-set + level + provider-set + provider + taskname + message + source + rules
        Assert.AreEqual(11, list.Count);
        Assert.AreEqual(list.Count, Labels(state).Count);
    }

    [TestMethod]
    public void Labels_ReadLikeTheDesign()
    {
        var labels = Labels(new ActiveFilterState
        {
            Search = "cache-miss",
            LevelSet = new[] { "Error", "Warning" },
            Provider = "Kernel",
            TimePreset = "24h",
        });
        CollectionAssert.AreEqual(
            new[] { "search: \"cache-miss\"", "time: last 24h", "level in (Error, Warning)", "provider ~ Kernel" },
            labels);
    }

    [TestMethod]
    public void Order_FollowsThePane_SearchTimeLevelFieldsRulesQuickRules()
    {
        var kinds = Build(new ActiveFilterState
        {
            Search = "x",
            From = new DateTime(2026, 1, 1),
            LevelSet = new[] { "Error" },
            Message = "boom",
            RuleFilterActive = true,
            RuleFileCount = 2,
            QuickRules = new[] { new QuickRuleView { Label = "strip msg ~ heartbeat", Enabled = true } },
        }).Select(f => f.Kind).ToList();

        CollectionAssert.AreEqual(
            new[] { "search", "time", "level", "field", "rules", "quickrule" }, kinds);
    }

    [TestMethod]
    public void TimePreset_IsOneConstraint_NotABareFromBound()
    {
        var withPreset = Build(new ActiveFilterState { TimePreset = "24h", From = new DateTime(2026, 1, 1) });
        Assert.AreEqual(1, withPreset.Count);
        Assert.AreEqual("time: last 24h", withPreset[0].Label);
        Assert.AreEqual("preset", withPreset[0].Key);
    }

    [TestMethod]
    public void AbsoluteRange_IsTwoIndependentlyRemovableBounds()
    {
        var list = Build(new ActiveFilterState
        {
            From = new DateTime(2026, 8, 4, 9, 41, 0),
            To = new DateTime(2026, 8, 4, 10, 0, 0),
        });
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual("from", list[0].Key);
        Assert.AreEqual("to", list[1].Key);
        StringAssert.Contains(list[0].Label, "2026-08-04 09:41");
        StringAssert.StartsWith(list[1].Label, "time ≤");
    }

    [TestMethod]
    public void FieldSet_AndSubstring_AreSeparatePills()
    {
        var list = Build(new ActiveFilterState { Provider = "Kernel", ProviderSet = new[] { "Kernel-Power", "Kernel-PnP" } });
        CollectionAssert.AreEqual(
            new[] { "provider in (Kernel-Power, Kernel-PnP)", "provider ~ Kernel" },
            list.Select(f => f.Label).ToList());
        // Distinct keys, so removing one leaves the other alone.
        Assert.AreEqual("provider:set", list[0].Key);
        Assert.AreEqual("provider", list[1].Key);
    }

    [TestMethod]
    public void LongValues_AreEllipsized()
    {
        var label = Labels(new ActiveFilterState { Message = new string('a', 80) })[0];
        Assert.IsTrue(label.Length < 40, "long values must not blow out the pill: " + label);
        StringAssert.EndsWith(label, "…");
    }

    [TestMethod]
    public void BigSets_NameAFewThenCount()
    {
        var label = Labels(new ActiveFilterState { SourceSet = new[] { "a", "b", "c", "d", "e" } })[0];
        Assert.AreEqual("source in (a, b, c, +2 more)", label);
    }

    [TestMethod]
    public void ExtraFields_AppearAfterTheBuiltInFour()
    {
        var list = Build(new ActiveFilterState
        {
            Provider = "Kernel",
            ExtraFields = new[]
            {
                new KeyValuePair<string, string>("processid", "1234"),
                new KeyValuePair<string, string>("channel", ""),   // empty = not a constraint
            },
        });
        CollectionAssert.AreEqual(new[] { "provider ~ Kernel", "processid ~ 1234" }, list.Select(f => f.Label).ToList());
        Assert.AreEqual("processid", list[1].Key);
    }

    [TestMethod]
    public void QuickRules_CountOnlyWhileEnabled()
    {
        var state = new ActiveFilterState
        {
            QuickRules = new[]
            {
                new QuickRuleView { Label = "strip msg ~ heartbeat", Enabled = true },
                new QuickRuleView { Label = "TaskName ← PID=(?<v>\\d+)", Enabled = false },
            },
        };
        var list = Build(state);
        Assert.AreEqual(1, list.Count, "a disabled quick rule reshapes nothing, so it isn't an active filter");
        Assert.AreEqual("rule: strip msg ~ heartbeat", list[0].Label);
        Assert.AreEqual("0", list[0].Key, "the key is the rule's index so the pill removes that exact rule");
    }

    [TestMethod]
    public void RuleFilter_PluralisesFileCount()
    {
        Assert.AreEqual("rules: 1 file", Labels(new ActiveFilterState { RuleFilterActive = true, RuleFileCount = 1 })[0]);
        Assert.AreEqual("rules: 2 files", Labels(new ActiveFilterState { RuleFilterActive = true, RuleFileCount = 2 })[0]);
    }

    [TestMethod]
    public void ClearAction_ComesFromTheHost_AndIsPerConstraint()
    {
        var cleared = new List<string>();
        var list = ActiveFilterCatalog.Build(
            new ActiveFilterState { Search = "x", Provider = "Kernel" },
            (kind, key) => () => cleared.Add(kind + "/" + key));

        list.Single(f => f.Kind == "field").Clear();
        CollectionAssert.AreEqual(new[] { "field/provider" }, cleared);
    }

    // ── A structured query is not a search term ───────────────────────────────
    // Labelling `msg == "FAILED" AND msg == "test"` as `search: "..."` reads as a literal string to look
    // for, and the label's own quotes collide with the query's. It stays ONE pill (you cannot remove one
    // side of a boolean expression) but it must say what it is, and the full text must survive on the
    // tooltip so an ellipsis never hides the meaning.

    [TestMethod]
    public void StructuredQuery_IsLabelledQuery_NotSearch()
    {
        var q = "msg == \"FAILED\" AND msg == \"test\"";
        var only = ActiveFilterCatalog.Build(new ActiveFilterState { Search = q, SearchIsQuery = true }).Single();

        StringAssert.StartsWith(only.Label, "query: ");
        Assert.IsFalse(only.Label.StartsWith("search:"), "a query is not a search term");
        Assert.AreEqual(q, only.FullText, "the untruncated query must be available for the tooltip");
    }

    [TestMethod]
    public void PlainText_IsStillLabelledSearch_AndQuoted()
    {
        var only = ActiveFilterCatalog.Build(new ActiveFilterState { Search = "cache-miss" }).Single();
        Assert.AreEqual("search: \"cache-miss\"", only.Label);
        Assert.AreEqual("", only.FullText, "nothing was hidden, so there is nothing to expand");
    }

    [TestMethod]
    public void Query_IsOneConstraint_NotDecomposedIntoItsTerms()
    {
        // Removing one half of an AND/OR is not a thing the UI can honestly offer.
        var built = ActiveFilterCatalog.Build(new ActiveFilterState
        {
            Search = "msg == \"a\" OR msg == \"b\"",
            SearchIsQuery = true,
        });
        Assert.AreEqual(1, built.Count);
    }

    [TestMethod]
    public void LongQuery_IsEllipsizedInTheLabel_ButWholeInFullText()
    {
        var q = "provider ~ Microsoft-Windows-Kernel-Power AND level == Error AND msg ~ transition";
        var only = ActiveFilterCatalog.Build(new ActiveFilterState { Search = q, SearchIsQuery = true }).Single();

        Assert.IsTrue(only.Label.Length < q.Length, "a long query should not be shown whole on the pill");
        StringAssert.EndsWith(only.Label, "…");
        Assert.AreEqual(q, only.FullText);
    }
}
