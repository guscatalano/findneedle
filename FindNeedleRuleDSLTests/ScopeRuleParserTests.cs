using System;
using System.Collections.Generic;
using System.Linq;
using FindNeedlePluginLib;
using FindNeedleRuleDSL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleRuleDSLTests;

/// <summary>
/// Unit tests for compiling RuleDSL `scope` sections into a decode-time <see cref="DecodeScope"/> and for
/// the loader validation that keeps a scope rule "pushdownable" (provider/time/level only — never message).
/// </summary>
[TestClass]
public class ScopeRuleParserTests
{
    private static UnifiedRuleSection ScopeSection(
        IEnumerable<string>? providers = null, string? mode = null,
        string? from = null, string? to = null, IEnumerable<string>? levels = null,
        string? match = null)
    {
        return new UnifiedRuleSection
        {
            Name = "scope",
            Purpose = "scope",
            Providers = providers?.ToList() ?? new List<string>(),
            Rules = new List<UnifiedRule>
            {
                new UnifiedRule
                {
                    Name = "scope",
                    Match = match ?? string.Empty,
                    Action = new UnifiedRuleAction
                    {
                        Type = "scope",
                        ProviderMode = mode,
                        TimeFrom = from,
                        TimeTo = to,
                        Levels = levels?.ToList(),
                    },
                },
            },
        };
    }

    private static DateTime Utc(int h) => new DateTime(2025, 7, 26, h, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void Build_ExcludeProviders_DropsThemKeepsRest()
    {
        var scope = ScopeRuleParser.Build(new[] { ScopeSection(new[] { "Windows Kernel", "MSNT_SystemTrace" }, mode: "exclude") });
        Assert.IsNotNull(scope);
        Assert.IsFalse(scope!.Keep("Windows Kernel", Utc(9), -1), "kernel excluded");
        Assert.IsFalse(scope.Keep("msnt_systemtrace", Utc(9), -1), "case-insensitive exclude");
        Assert.IsTrue(scope.Keep("Microsoft-Windows-RPC", Utc(9), -1), "other providers kept");
    }

    [TestMethod]
    public void Build_IncludeProviders_KeepsOnlyThose()
    {
        var scope = ScopeRuleParser.Build(new[] { ScopeSection(new[] { "Microsoft-Windows-DotNETRuntime" }) }); // default include
        Assert.IsNotNull(scope);
        Assert.IsTrue(scope!.Keep("Microsoft-Windows-DotNETRuntime", Utc(9), -1));
        Assert.IsFalse(scope.Keep("Windows Kernel", Utc(9), -1));
    }

    [TestMethod]
    public void Build_TimeWindow_FiltersByTimestamp()
    {
        var scope = ScopeRuleParser.Build(new[] { ScopeSection(from: "2025-07-26T08:00:00Z", to: "2025-07-26T10:00:00Z") });
        Assert.IsNotNull(scope);
        Assert.IsFalse(scope!.Keep("X", Utc(7), -1), "before window");
        Assert.IsTrue(scope.Keep("X", Utc(9), -1), "inside window");
        Assert.IsFalse(scope.Keep("X", Utc(11), -1), "after window");
    }

    [TestMethod]
    public void Build_NoPredicates_ReturnsNull()
    {
        Assert.IsNull(ScopeRuleParser.Build(new[] { ScopeSection() }), "empty scope = load everything");
        Assert.IsNull(ScopeRuleParser.Build(null));
    }

    [TestMethod]
    public void Build_TwoSections_AndCombinesProviders()
    {
        // include {A,B} AND include {B,C} -> only B passes
        var s1 = ScopeSection(new[] { "A", "B" });
        var s2 = ScopeSection(new[] { "B", "C" });
        var scope = ScopeRuleParser.Build(new[] { s1, s2 });
        Assert.IsNotNull(scope);
        Assert.IsTrue(scope!.Keep("B", Utc(9), -1));
        Assert.IsFalse(scope.Keep("A", Utc(9), -1));
        Assert.IsFalse(scope.Keep("C", Utc(9), -1));
    }

    [TestMethod]
    public void Validate_RejectsMatchOnMessage()
    {
        var errors = ScopeRuleParser.Validate(new[] { ScopeSection(new[] { "X" }, match: "some message text") });
        Assert.IsTrue(errors.Count > 0 && errors[0].Contains("cannot test message"), "match/unmatch must be rejected for scope");
    }

    [TestMethod]
    public void Validate_RejectsBadTimeAndLevel()
    {
        var errors = ScopeRuleParser.Validate(new[] { ScopeSection(from: "not-a-date", levels: new[] { "Bogus" }) });
        Assert.IsTrue(errors.Any(e => e.Contains("timeFrom")), "bad time rejected");
        Assert.IsTrue(errors.Any(e => e.Contains("unknown level")), "bad level rejected");
    }

    [TestMethod]
    public void Validate_CleanScope_NoErrors()
    {
        var errors = ScopeRuleParser.Validate(new[] { ScopeSection(new[] { "Windows Kernel" }, mode: "exclude", from: "2025-07-26T08:00:00Z", levels: new[] { "Error", "Warning" }) });
        Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
    }
}
