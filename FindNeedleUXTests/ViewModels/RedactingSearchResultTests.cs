using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using FindNeedlePluginLib;
using FindNeedleUX.Pages.NativeResultViewer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="RedactingSearchResult"/> — masks matched PII in a row's text fields while
/// passing non-text fields through. Backs the "Redact PII" rule.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class RedactingSearchResultTests
{
    private static RedactingSearchResult Wrap(ISearchResult inner, params (string pattern, string repl)[] rules)
    {
        var compiled = new List<(Regex, string)>();
        foreach (var (p, r) in rules) compiled.Add((new Regex(p, RegexOptions.IgnoreCase), r));
        return new RedactingSearchResult(inner, compiled);
    }

    [TestMethod]
    public void Masks_Email_InMessageAndSearchableData()
    {
        var r = Wrap(new R("contact jane.doe@contoso.com now"),
            (@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", "[REDACTED-EMAIL]"));

        StringAssert.Contains(r.GetMessage(), "[REDACTED-EMAIL]");
        Assert.IsFalse(r.GetMessage().Contains("jane.doe@contoso.com"));
        Assert.IsFalse(r.GetSearchableData().Contains("contoso"), "search text is masked too, so search can't leak it");
    }

    [TestMethod]
    public void AppliesMultipleRules()
    {
        var r = Wrap(new R("user a@b.co called 425-555-0100"),
            (@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", "[E]"),
            (@"\b\d{3}-\d{3}-\d{4}\b", "[P]"));
        Assert.AreEqual("user [E] called [P]", r.GetMessage());
    }

    [TestMethod]
    public void PassesThroughNonTextFields()
    {
        var when = new DateTime(2026, 6, 20, 1, 2, 3, DateTimeKind.Utc);
        var r = Wrap(new R("nothing here", level: Level.Error, time: when), (@"zzz", "x"));
        Assert.AreEqual(Level.Error, r.GetLevel());
        Assert.AreEqual(when, r.GetLogTime());
        Assert.AreEqual("nothing here", r.GetMessage(), "no match → unchanged");
    }

    [TestMethod]
    public void NoRules_LeavesTextUntouched()
    {
        var r = Wrap(new R("a@b.com"));
        Assert.AreEqual("a@b.com", r.GetMessage());
    }

    private sealed class R : ISearchResult
    {
        private readonly string _m;
        private readonly Level _level;
        private readonly DateTime _time;
        public R(string m, Level level = Level.Info, DateTime time = default)
        { _m = m; _level = level; _time = time == default ? new DateTime(2026, 1, 1) : time; }
        public DateTime GetLogTime() => _time;
        public string GetMachineName() => "M";
        public void WriteToConsole() { }
        public Level GetLevel() => _level;
        public string GetUsername() => "u";
        public string GetTaskName() => "t";
        public string GetOpCode() => "";
        public string GetSource() => "s";
        public string GetSearchableData() => _m;
        public string GetMessage() => _m;
        public string GetResultSource() => "rs";
    }
}
