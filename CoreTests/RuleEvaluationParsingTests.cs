using System;
using findneedle.RuleDSL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// Tests for the relative-time parsing in <see cref="RuleEvaluationEngine"/> that backs RuleDSL date-range
/// conditions ("withinLast": "1h", "after": "-30d", "before": ISO date).
/// </summary>
[TestClass]
public class RuleEvaluationParsingTests
{
    [DataTestMethod]
    [DataRow("1h", 3600)]
    [DataRow("24h", 86400)]
    [DataRow("30m", 1800)]
    [DataRow("7d", 604800)]
    [DataRow("2H", 7200)]   // case-insensitive
    public void ParseTimeSpan_ParsesSuffixedDurations(string input, int expectedSeconds)
        => Assert.AreEqual(expectedSeconds, (int)RuleEvaluationEngine.ParseTimeSpan(input).TotalSeconds);

    [DataTestMethod]
    [DataRow("")]
    [DataRow(null)]
    [DataRow("garbage")]
    [DataRow("5x")]   // unrecognized unit
    [DataRow("h")]    // no number
    public void ParseTimeSpan_InvalidInput_IsZero(string input)
        => Assert.AreEqual(TimeSpan.Zero, RuleEvaluationEngine.ParseTimeSpan(input));

    [TestMethod]
    public void ParseDateTime_NullOrEmpty_IsNull()
    {
        Assert.IsNull(RuleEvaluationEngine.ParseDateTime(null));
        Assert.IsNull(RuleEvaluationEngine.ParseDateTime(""));
    }

    [TestMethod]
    public void ParseDateTime_AbsoluteDate_Parses()
    {
        var d = RuleEvaluationEngine.ParseDateTime("2026-03-15");
        Assert.IsTrue(d.HasValue);
        // Parsed as UTC (AssumeUniversal); compare in UTC so the assert is timezone-independent.
        var u = d.Value.ToUniversalTime();
        Assert.AreEqual(2026, u.Year);
        Assert.AreEqual(3, u.Month);
        Assert.AreEqual(15, u.Day);
    }

    [TestMethod]
    public void ParseDateTime_Relative_SubtractsFromNow()
    {
        var d = RuleEvaluationEngine.ParseDateTime("-30d");
        Assert.IsTrue(d.HasValue);
        Assert.AreEqual(30.0, (DateTime.Now - d.Value).TotalDays, 0.05); // ~30 days ago
    }

    [TestMethod]
    public void ParseDateTime_Garbage_IsNull()
        => Assert.IsNull(RuleEvaluationEngine.ParseDateTime("not a date at all"));
}
