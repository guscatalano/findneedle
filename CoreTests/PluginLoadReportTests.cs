using System;
using System.Collections.Generic;
using findneedle.PluginSubsystem;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// Saying "the plugins did not load" instead of "your log is empty".
///
/// The shipped MSI and release.zip had no plugin loader: every plugin threw, no file-type processor
/// registered, and opening any log reported "This search returned no rows - the source was empty, or
/// nothing matched/decoded". The app blamed the file for its own missing dependency, which is how the
/// bug survived a release. These pin the wording that tells the two apart.
/// </summary>
[TestClass]
public class PluginLoadReportTests
{
    private static IReadOnlyList<PluginLoadFailure> None => Array.Empty<PluginLoadFailure>();

    [TestMethod]
    public void EverythingLoaded_SaysNothing()
        => Assert.IsNull(PluginLoadReport.Headline(loadedModules: 7, None, extensionProcessors: 7),
            "a healthy load must not put a warning in front of the user");

    [TestMethod]
    public void NoParsersAndAMissingLoader_NamesTheLoader()
    {
        var failures = new[] { new PluginLoadFailure("BasicTextPlugin.dll", "can't find fake loader for plugins") };
        var line = PluginLoadReport.Headline(0, failures, 0);
        StringAssert.Contains(line, "No log parsers loaded");
        StringAssert.Contains(line, "FakeLoadPlugin.exe", "the actionable detail is WHICH file is missing");
    }

    [TestMethod]
    public void NoParsersForAnotherReason_StillSaysNoParsers_WithoutGuessingWhy()
    {
        var failures = new[] { new PluginLoadFailure("BasicTextPlugin.dll", "Could not load file or assembly") };
        var line = PluginLoadReport.Headline(0, failures, 0);
        StringAssert.Contains(line, "No log parsers loaded");
        Assert.IsFalse(line.Contains("FakeLoadPlugin", StringComparison.OrdinalIgnoreCase),
            "do not blame the loader when the loader is not the problem");
    }

    [TestMethod]
    public void SomeLoadedSomeNot_IsAWarning_NotACatastrophe()
    {
        var one = PluginLoadReport.Headline(6, new[] { new PluginLoadFailure("PcapPlugin.dll", "boom") }, 5);
        StringAssert.Contains(one, "1 plugin failed");
        StringAssert.Contains(one, "PcapPlugin.dll", "name the one that failed when there is only one");
        Assert.IsFalse(one.Contains("No log parsers loaded", StringComparison.Ordinal));

        var many = PluginLoadReport.Headline(4, new[]
        {
            new PluginLoadFailure("PcapPlugin.dll", "boom"),
            new PluginLoadFailure("KustoPlugin.dll", "boom"),
        }, 5);
        StringAssert.Contains(many, "2 plugins failed");
    }

    [TestMethod]
    public void NothingLoadedAtAll_CountsAsNoParsers_EvenWithNoRecordedFailures()
        => StringAssert.Contains(PluginLoadReport.Headline(0, None, 0), "No log parsers loaded",
            "a config that loaded nothing is just as broken as one that threw");

    [TestMethod]
    public void RecordingFailures_FeedsTheHeadlineAndTheDetail()
    {
        PluginLoadReport.Reset();
        Assert.AreEqual(0, PluginLoadReport.GetFailures().Count);
        Assert.IsNull(PluginLoadReport.HeadlineFor(extensionProcessors: 3), "nothing recorded, nothing to say");

        PluginLoadReport.RecordLoaded();
        PluginLoadReport.RecordFailure("ZipFilePlugin.dll", "can't find fake loader for plugins");
        Assert.AreEqual(1, PluginLoadReport.GetFailures().Count);
        StringAssert.Contains(PluginLoadReport.HeadlineFor(0), "FakeLoadPlugin.exe");
        StringAssert.Contains(PluginLoadReport.Detail(), "ZipFilePlugin.dll: can't find fake loader");

        PluginLoadReport.Reset();
        Assert.AreEqual("", PluginLoadReport.Detail(), "a fresh load starts from a clean slate");
    }

    [TestMethod]
    public void ABlankPluginName_IsIgnored()
    {
        PluginLoadReport.Reset();
        PluginLoadReport.RecordFailure("   ", "whatever");
        Assert.AreEqual(0, PluginLoadReport.GetFailures().Count);
    }
}
