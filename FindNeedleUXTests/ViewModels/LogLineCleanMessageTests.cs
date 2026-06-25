using System;
using FindNeedleUX;
using FindNeedlePluginLib;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.ViewModels;

/// <summary>
/// Tests for <see cref="LogLine.CleanMessage"/> (strip a leading [timestamp] / LEVEL: that duplicate
/// the Time/Level columns) and the NOT_SUPPORTED normalization done in the LogLine constructor.
/// </summary>
[TestClass]
[TestCategory("ViewModel")]
public class LogLineCleanMessageTests
{
    private static readonly DateTime T = new(2026, 3, 1, 9, 0, 0);

    [TestMethod]
    public void CleanMessage_StripsMatchingTimestampAndLevel()
        => Assert.AreEqual("hello world",
            LogLine.CleanMessage("[2026-03-01 09:00:00] INFO: hello world", T, "Info"));

    [TestMethod]
    public void CleanMessage_StripsLevelPrefixOnly()
        => Assert.AreEqual("disk full", LogLine.CleanMessage("ERROR: disk full", DateTime.MinValue, "Error"));

    [TestMethod]
    public void CleanMessage_HonorsLevelSynonyms()
    {
        Assert.AreEqual("low space", LogLine.CleanMessage("WARN: low space", DateTime.MinValue, "Warning"));
        Assert.AreEqual("low space", LogLine.CleanMessage("WARNING: low space", DateTime.MinValue, "Warning"));
    }

    [TestMethod]
    public void CleanMessage_LeavesPlainMessageUntouched()
        => Assert.AreEqual("just a message", LogLine.CleanMessage("just a message", T, "Info"));

    [TestMethod]
    public void CleanMessage_DoesNotStripNonMatchingBracket()
    {
        // A leading bracket that isn't the row's timestamp must be preserved.
        const string m = "[component] did a thing";
        Assert.AreEqual(m, LogLine.CleanMessage(m, T, "Info"));
    }

    [TestMethod]
    public void CleanMessage_NullOrEmpty_ReturnedAsIs()
    {
        Assert.AreEqual("", LogLine.CleanMessage("", T, "Info"));
        Assert.IsNull(LogLine.CleanMessage(null, T, "Info"));
    }

    [TestMethod]
    public void Constructor_NormalizesNotSupportedSentinelToEmpty()
    {
        var line = new LogLine(new SentinelResult(), 0);
        Assert.AreEqual("", line.Provider, "the legacy NOT_SUPPORTED sentinel must not leak into a column");
        Assert.AreEqual("", line.TaskName);
    }

    // Returns the legacy NOT_SUPPORTED sentinel for the source/task fields.
    private sealed class SentinelResult : ISearchResult
    {
        public DateTime GetLogTime() => DateTime.MinValue;
        public string GetMachineName() => ISearchResult.NOT_SUPPORTED;
        public void WriteToConsole() { }
        public Level GetLevel() => Level.Info;
        public string GetUsername() => ISearchResult.NOT_SUPPORTED;
        public string GetTaskName() => ISearchResult.NOT_SUPPORTED;
        public string GetOpCode() => ISearchResult.NOT_SUPPORTED;
        public string GetSource() => ISearchResult.NOT_SUPPORTED;
        public string GetSearchableData() => "data";
        public string GetMessage() => "msg";
        public string GetResultSource() => ISearchResult.NOT_SUPPORTED;
    }
}
