using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using FindNeedleRuleDSL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleRuleDSLTests;

/// <summary>
/// Validates the shipped PII filter rule set (CommonRules/pii-filter.rules.json): it parses as a
/// UnifiedRuleSet of exclude rules, each pattern compiles, and the patterns match representative PII
/// while leaving clean lines alone. This is the engine-agnostic core of "a RuleDSL that filters out PII"
/// — the viewer's rule-view filter compiles these same Match/exclude rules.
/// </summary>
[TestClass]
public class PiiFilterRulesTests
{
    private static UnifiedRuleSet LoadRules()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "FindNeedleUX", "CommonRules", "pii-filter.rules.json");
            if (File.Exists(candidate))
                return JsonSerializer.Deserialize<UnifiedRuleSet>(File.ReadAllText(candidate),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            dir = dir.Parent;
        }
        Assert.Fail("pii-filter.rules.json not found walking up from " + Directory.GetCurrentDirectory());
        return null;
    }

    private static Regex Rule(UnifiedRuleSet set, string name)
    {
        var rule = set.Sections.SelectMany(s => s.Rules).First(r => r.Name == name);
        Assert.AreEqual("exclude", rule.Action.Type, $"{name} should be an exclude rule");
        return new Regex(rule.Match, RegexOptions.IgnoreCase);
    }

    [TestMethod]
    public void RuleSet_ParsesAsExcludeFilter()
    {
        var set = LoadRules();
        Assert.IsNotNull(set);
        var rules = set.Sections.SelectMany(s => s.Rules).ToList();
        Assert.IsTrue(rules.Count >= 5, "expect email/ssn/card/phone/mac rules");
        Assert.IsTrue(rules.All(r => r.Action.Type == "exclude"), "every rule excludes");
        // Every pattern must compile.
        foreach (var r in rules)
            _ = new Regex(r.Match);
    }

    [TestMethod]
    public void Email_Matches_PiiLine_NotCleanLine()
    {
        var rx = Rule(LoadRules(), "exclude-email");
        Assert.IsTrue(rx.IsMatch("2026-06-20 user jane.doe@contoso.com logged in"));
        Assert.IsFalse(rx.IsMatch("2026-06-20 user logged in successfully"));
    }

    [TestMethod]
    public void Ssn_Matches_FormattedSsn()
    {
        var rx = Rule(LoadRules(), "exclude-ssn");
        Assert.IsTrue(rx.IsMatch("record SSN 123-45-6789 processed"));
        Assert.IsFalse(rx.IsMatch("error code 12-345-6789 raised"));
    }

    [TestMethod]
    public void CreditCard_Matches_FormattedCard()
    {
        var rx = Rule(LoadRules(), "exclude-credit-card");
        Assert.IsTrue(rx.IsMatch("charged 4111 1111 1111 1111 ok"));
        Assert.IsTrue(rx.IsMatch("card 4111-1111-1111-1111"));
        Assert.IsFalse(rx.IsMatch("order 12345 shipped"));
    }

    [TestMethod]
    public void Phone_Matches_UsPhone()
    {
        var rx = Rule(LoadRules(), "exclude-phone");
        Assert.IsTrue(rx.IsMatch("call (425) 555-0100 for help"));
        Assert.IsTrue(rx.IsMatch("contact 425-555-0100"));
    }

    [TestMethod]
    public void Mac_Matches_MacAddress()
    {
        var rx = Rule(LoadRules(), "exclude-mac-address");
        Assert.IsTrue(rx.IsMatch("nic 00:1A:2B:3C:4D:5E up"));
        Assert.IsFalse(rx.IsMatch("ratio 12:34 reached"));
    }

    [TestMethod]
    public void Ipv4Rule_ShipsDisabled_ToAvoidNukingNetworkLogs()
    {
        var rule = LoadRules().Sections.SelectMany(s => s.Rules).First(r => r.Name == "exclude-ipv4");
        Assert.IsFalse(rule.Enabled, "IPv4 filtering is opt-in (off by default)");
    }

    // ----- companion redact rule set -----

    private static UnifiedRuleSet LoadRedactRules()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "FindNeedleUX", "CommonRules", "pii-redact.rules.json");
            if (File.Exists(candidate))
                return JsonSerializer.Deserialize<UnifiedRuleSet>(File.ReadAllText(candidate),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            dir = dir.Parent;
        }
        Assert.Fail("pii-redact.rules.json not found");
        return null;
    }

    [TestMethod]
    public void RedactRuleSet_UsesRedactAction_WithReplacement()
    {
        var set = LoadRedactRules();
        var rules = set.Sections.SelectMany(s => s.Rules).ToList();
        Assert.IsTrue(rules.Count >= 5);
        foreach (var r in rules)
        {
            Assert.AreEqual("redact", r.Action.Type, $"{r.Name} should redact");
            Assert.IsFalse(string.IsNullOrEmpty(r.Action.Replacement), $"{r.Name} needs a replacement mask");
            _ = new Regex(r.Match); // must compile
        }
    }

    [TestMethod]
    public void RedactEmail_MasksTheValue()
    {
        var rule = LoadRedactRules().Sections.SelectMany(s => s.Rules).First(r => r.Name == "redact-email");
        var rx = new Regex(rule.Match, RegexOptions.IgnoreCase);
        var masked = rx.Replace("login from bob@contoso.com ok", rule.Action.Replacement);
        Assert.IsFalse(masked.Contains("bob@contoso.com"));
        StringAssert.Contains(masked, rule.Action.Replacement);
    }
}
