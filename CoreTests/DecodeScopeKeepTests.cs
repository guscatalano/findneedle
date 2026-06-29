using System;
using System.Collections.Generic;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// Unit contract for <see cref="DecodeScope.Keep"/> — in particular the "unknown dimension is not filtered"
/// rule that lets one compiled scope apply across formats that expose different fields: a null provider
/// (plain text) skips the provider lists, a null timestamp (un-timestamped line) skips the time window, and
/// a negative level (decoder that can't cheaply get the level — ETL/EVTX) skips the level set.
/// </summary>
[TestClass]
public class DecodeScopeKeepTests
{
    private static HashSet<string> P(params string[] s) => new(s, StringComparer.OrdinalIgnoreCase);
    private static readonly DateTime T = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void NullProvider_SkipsProviderLists()
    {
        var inc = new DecodeScope { IncludeProviders = P("A") };
        Assert.IsFalse(inc.Keep("B", T, -1), "non-null provider not in the allow-list is dropped");
        Assert.IsTrue(inc.Keep(null, T, -1), "null provider skips the allow-list (text has no provider)");

        var exc = new DecodeScope { ExcludeProviders = P("A") };
        Assert.IsFalse(exc.Keep("A", T, -1), "excluded provider is dropped");
        Assert.IsTrue(exc.Keep(null, T, -1), "null provider skips the drop-list");
    }

    [TestMethod]
    public void NullTimestamp_SkipsTimeWindow()
    {
        var scope = new DecodeScope { FromUtc = T };
        Assert.IsFalse(scope.Keep(null, T.AddHours(-1), -1), "a real timestamp before the window is dropped");
        Assert.IsTrue(scope.Keep(null, null, -1), "null timestamp skips the window (un-timestamped line kept)");
    }

    [TestMethod]
    public void NegativeLevel_SkipsLevelSet()
    {
        var scope = new DecodeScope { Levels = new HashSet<int> { 2 } };
        Assert.IsFalse(scope.Keep(null, T, 4), "a known level not in the set is dropped");
        Assert.IsTrue(scope.Keep(null, T, -1), "level < 0 (unknown) skips the level set");
        Assert.IsTrue(scope.Keep(null, T, 2), "a known level in the set passes");
    }

    [TestMethod]
    public void TimeWindow_BoundsAreInclusive()
    {
        var scope = new DecodeScope { FromUtc = T, ToUtc = T.AddHours(1) };
        Assert.IsTrue(scope.Keep(null, T, -1), "lower bound is inclusive");
        Assert.IsTrue(scope.Keep(null, T.AddHours(1), -1), "upper bound is inclusive");
        Assert.IsFalse(scope.Keep(null, T.AddMinutes(-1), -1), "before the window is dropped");
        Assert.IsFalse(scope.Keep(null, T.AddHours(2), -1), "after the window is dropped");
    }
}
