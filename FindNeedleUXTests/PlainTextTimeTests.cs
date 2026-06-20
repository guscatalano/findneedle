using System;
using BasicTextPlugin;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests;

/// <summary>
/// Plain-text timestamp parsing: logs come with the time bracketed, bare, or ISO. GetLogTime must
/// read all of those (it previously only handled "[…]" so bare-timestamp logs showed no time).
/// </summary>
[TestClass]
public class PlainTextTimeTests
{
    private static DateTime Time(string line) => new PlainTextSearchResult { Text = line }.GetLogTime();

    [TestMethod]
    public void Bracketed_Parses()
    {
        var t = Time("[2026-06-19 08:00:01] INFO something happened");
        Assert.AreEqual(new DateTime(2026, 6, 19, 8, 0, 1), t);
    }

    [TestMethod]
    public void BareLeadingTimestamp_Parses()
    {
        var t = Time("2026-06-19 08:00:01.001 INFO  App: starting");
        Assert.AreEqual(2026, t.Year);
        Assert.AreEqual(8, t.Hour);
        Assert.AreEqual(1, t.Second);
    }

    [TestMethod]
    public void IsoTimestamp_Parses()
    {
        var t = Time("2026-06-19T08:00:01.5000000Z worker tick");
        Assert.AreEqual(2026, t.ToUniversalTime().Year);
        Assert.AreEqual(8, t.ToUniversalTime().Hour);
    }

    [TestMethod]
    public void NoTimestamp_IsMinValue()
    {
        Assert.AreEqual(DateTime.MinValue, Time("just a plain message with no timestamp"));
    }
}
