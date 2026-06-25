using FindNeedlePluginUtils.StructuredLog;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedlePluginUtilsTests;

/// <summary>
/// Tests for <see cref="StructuredLogFieldMapper.AutoDetect"/> — maps a CSV/JSON header name to a
/// canonical field (time/level/message/provider/…) via the alias table, or null when unrecognized.
/// </summary>
[TestClass]
public class StructuredLogFieldMapperAutoDetectTests
{
    [DataTestMethod]
    [DataRow("time", "time")]
    [DataRow("timestamp", "time")]
    [DataRow("date", "time")]
    [DataRow("level", "level")]
    [DataRow("severity", "level")]
    [DataRow("message", "message")]
    [DataRow("msg", "message")]
    [DataRow("provider", "provider")]
    [DataRow("logger", "provider")]
    public void AutoDetect_MapsKnownAliases(string header, string expected)
        => Assert.AreEqual(expected, StructuredLogFieldMapper.AutoDetect(header));

    [TestMethod]
    public void AutoDetect_UnknownHeader_ReturnsNull()
    {
        Assert.IsNull(StructuredLogFieldMapper.AutoDetect("totally_custom_column"));
        Assert.IsNull(StructuredLogFieldMapper.AutoDetect(""));
        Assert.IsNull(StructuredLogFieldMapper.AutoDetect(null));
    }
}
