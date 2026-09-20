using System;
using System.Linq;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUX.UnitTests.Services;

/// <summary>
/// Saying which zone the Time column is in - and never guessing when the rows don't say.
/// </summary>
[TestClass]
public class TimeZoneLabelTests
{
    private static DateTime At(int hour, DateTimeKind kind) => new(2026, 3, 1, hour, 0, 0, kind);

    [TestMethod]
    public void AllUtcRows_ReadAsUtc()
        => Assert.AreEqual(TimeZoneLabel.Utc, TimeZoneLabel.For(new[] { At(9, DateTimeKind.Utc), At(10, DateTimeKind.Utc) }));

    [TestMethod]
    public void AllLocalRows_ReadAsLocal()
        => Assert.AreEqual(TimeZoneLabel.Local, TimeZoneLabel.For(new[] { At(9, DateTimeKind.Local) }));

    [TestMethod]
    public void RowsWithNoZone_AreNotGuessedAt()
        => Assert.AreEqual(TimeZoneLabel.AsRecorded, TimeZoneLabel.For(new[] { At(9, DateTimeKind.Unspecified) }),
            "a text log carries no zone; claiming one would be a lie");

    [TestMethod]
    public void TwoSourcesDisagreeing_SayMixed()
    {
        Assert.AreEqual(TimeZoneLabel.Mixed, TimeZoneLabel.For(new[] { At(9, DateTimeKind.Utc), At(9, DateTimeKind.Local) }),
            "an ETW log and an Event Log opened together are an hour apart on screen - say so");
        Assert.AreEqual(TimeZoneLabel.Mixed, TimeZoneLabel.For(new[] { At(9, DateTimeKind.Utc), At(9, DateTimeKind.Unspecified) }),
            "some rows knowing and others not is still not 'UTC'");
    }

    [TestMethod]
    public void RowsWithoutATimestamp_DoNotVote()
    {
        Assert.AreEqual(TimeZoneLabel.Utc, TimeZoneLabel.For(new[] { default, At(9, DateTimeKind.Utc) }),
            "a row with no time at all says nothing about the zone");
        Assert.AreEqual(TimeZoneLabel.AsRecorded, TimeZoneLabel.For(Enumerable.Empty<DateTime>()));
        Assert.AreEqual(TimeZoneLabel.AsRecorded, TimeZoneLabel.For(null));
    }

    [TestMethod]
    public void TheHeader_OnlyCarriesAMarkerWhenItMeansSomething()
    {
        Assert.AreEqual("Time (UTC)", TimeZoneLabel.Header(TimeZoneLabel.Utc));
        Assert.AreEqual("Time (local)", TimeZoneLabel.Header(TimeZoneLabel.Local));
        Assert.AreEqual("Time (mixed zones)", TimeZoneLabel.Header(TimeZoneLabel.Mixed));
        Assert.AreEqual("Time", TimeZoneLabel.Header(TimeZoneLabel.AsRecorded), "no zone to state → no noise in the header");
        Assert.AreEqual("Time", TimeZoneLabel.Header(""));
    }

    [TestMethod]
    public void Describe_NamesTheZoneOfOneRow()
    {
        StringAssert.EndsWith(TimeZoneLabel.Describe(At(9, DateTimeKind.Utc)), " UTC");
        StringAssert.Contains(TimeZoneLabel.Describe(At(9, DateTimeKind.Local)), "local");
        StringAssert.Contains(TimeZoneLabel.Describe(At(9, DateTimeKind.Unspecified)), "no zone recorded");
        StringAssert.StartsWith(TimeZoneLabel.Describe(At(9, DateTimeKind.Utc)), "2026-03-01 09:00:00");
        Assert.AreEqual("", TimeZoneLabel.Describe(default));
    }
}
