using System;
using System.Globalization;
using FindNeedlePluginLib;
using KustoPlugin.FileExtension;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace KustoPluginTests;

[TestClass]
public class KustoExportLogLineTests
{
    [TestMethod]
    public void Parse_ValidLine_ReturnsLogLine()
    {
        var headers = new[] { "PreciseTimeStamp", "ProviderName", "Message" };
        var line = "2024-01-01 10:00:00\tTestProvider\tTest message";
        var fileName = "test.txt";

        var logLine = KustoExportLogLine.Parse(line, headers, fileName);

        Assert.IsNotNull(logLine);
        Assert.AreEqual("TestProvider", logLine.ProviderName);
        Assert.AreEqual("Test message", logLine.Message);
        Assert.AreEqual(fileName, logLine.FileName);
    }

    [TestMethod]
    public void Parse_MisalignedColumns_ReturnsNull()
    {
        var headers = new[] { "PreciseTimeStamp", "ProviderName", "Message" };
        var line = "2024-01-01 10:00:00\tTestProvider"; // Missing one column

        var logLine = KustoExportLogLine.Parse(line, headers, "test.txt");

        Assert.IsNull(logLine);
    }

    [TestMethod]
    public void Parse_ValidDatetime_ParsesCorrectly()
    {
        var headers = new[] { "PreciseTimeStamp", "Message" };
        var line = "2024-01-15 14:30:45\tTest";

        var logLine = KustoExportLogLine.Parse(line, headers, "test.txt");

        Assert.IsNotNull(logLine);
        Assert.AreEqual(new DateTime(2024, 1, 15, 14, 30, 45), logLine.PreciseTimeStamp);
    }

    [TestMethod]
    public void Parse_InvalidDatetime_UsesDefaultDate()
    {
        var headers = new[] { "PreciseTimeStamp", "Message" };
        var line = "not-a-date\tTest";

        var logLine = KustoExportLogLine.Parse(line, headers, "test.txt");

        Assert.IsNotNull(logLine);
        Assert.AreEqual(default(DateTime), logLine.PreciseTimeStamp);
    }

    [TestMethod]
    public void Parse_AllFields_PopulatesAllProperties()
    {
        var headers = new[] { "PreciseTimeStamp", "ActivityId", "Pid", "ProviderName", "TaskName", "Message", "EventMessage", "Level", "HostInstance" };
        var line = "2024-01-01 10:00:00\t{activity-id}\t1234\tMyProvider\tMyTask\tRaw Message\tEvent Message\t2\tMachine1";

        var logLine = KustoExportLogLine.Parse(line, headers, "test.txt");

        Assert.IsNotNull(logLine);
        Assert.AreEqual("{activity-id}", logLine.ActivityId);
        Assert.AreEqual("1234", logLine.Pid);
        Assert.AreEqual("MyProvider", logLine.ProviderName);
        Assert.AreEqual("MyTask", logLine.TaskName);
        Assert.AreEqual("Raw Message", logLine.Message);
        Assert.AreEqual("Event Message", logLine.EventMessage);
        Assert.AreEqual("2", logLine.Level);
        Assert.AreEqual("Machine1", logLine.HostInstance);
    }

    [TestMethod]
    public void Parse_UnknownColumn_IgnoresIt()
    {
        var headers = new[] { "PreciseTimeStamp", "UnknownColumn", "Message" };
        var line = "2024-01-01 10:00:00\tunknown value\tTest message";

        var logLine = KustoExportLogLine.Parse(line, headers, "test.txt");

        Assert.IsNotNull(logLine);
        Assert.AreEqual("Test message", logLine.Message);
    }

    [TestMethod]
    public void GetLogTime_ReturnsTimeStamp()
    {
        var logLine = new KustoExportLogLine
        {
            PreciseTimeStamp = new DateTime(2024, 1, 15, 14, 30, 45)
        };

        Assert.AreEqual(new DateTime(2024, 1, 15, 14, 30, 45), logLine.GetLogTime());
    }

    [TestMethod]
    public void GetMachineName_ReturnsHostInstance()
    {
        var logLine = new KustoExportLogLine { HostInstance = "MyMachine" };

        Assert.AreEqual("MyMachine", logLine.GetMachineName());
    }

    [TestMethod]
    public void GetLevel_WithNumericLevel_ReturnsCorrectLevel()
    {
        var testCases = new[]
        {
            ("1", FindNeedlePluginLib.Level.Catastrophic),
            ("2", FindNeedlePluginLib.Level.Error),
            ("3", FindNeedlePluginLib.Level.Warning),
            ("4", FindNeedlePluginLib.Level.Info),
            ("5", FindNeedlePluginLib.Level.Verbose),
        };

        foreach (var (levelStr, expectedLevel) in testCases)
        {
            var logLine = new KustoExportLogLine { Level = levelStr };
            Assert.AreEqual(expectedLevel, logLine.GetLevel(), $"Failed for level {levelStr}");
        }
    }

    [TestMethod]
    public void GetLevel_WithUnknownLevel_ReturnsInfo()
    {
        var logLine = new KustoExportLogLine { Level = "999" };
        Assert.AreEqual(FindNeedlePluginLib.Level.Info, logLine.GetLevel());
    }

    [TestMethod]
    public void GetLevel_WithoutLevel_ReturnsInfo()
    {
        var logLine = new KustoExportLogLine();
        Assert.AreEqual(FindNeedlePluginLib.Level.Info, logLine.GetLevel());
    }

    [TestMethod]
    public void GetUsername_ReturnsEmpty()
    {
        var logLine = new KustoExportLogLine();
        Assert.AreEqual(string.Empty, logLine.GetUsername());
    }

    [TestMethod]
    public void GetTaskName_ReturnsTaskName()
    {
        var logLine = new KustoExportLogLine { TaskName = "MyTask" };
        Assert.AreEqual("MyTask", logLine.GetTaskName());
    }

    [TestMethod]
    public void GetOpCode_ReturnsEmpty()
    {
        var logLine = new KustoExportLogLine();
        Assert.AreEqual(string.Empty, logLine.GetOpCode());
    }

    [TestMethod]
    public void GetSource_ReturnsProviderName()
    {
        var logLine = new KustoExportLogLine { ProviderName = "MyProvider" };
        Assert.AreEqual("MyProvider", logLine.GetSource());
    }

    [TestMethod]
    public void GetSearchableData_CombinesMessageAndEventMessage()
    {
        var logLine = new KustoExportLogLine
        {
            Message = "Raw message",
            EventMessage = "Event message"
        };

        var searchable = logLine.GetSearchableData();
        Assert.AreEqual("Raw message Event message", searchable);
    }

    [TestMethod]
    public void GetSearchableData_WithOnlyMessage_ReturnsMessage()
    {
        var logLine = new KustoExportLogLine { Message = "Raw message" };
        Assert.AreEqual("Raw message ", logLine.GetSearchableData());
    }

    [TestMethod]
    public void GetSearchableData_WithEmptyFields_ReturnsEmptyString()
    {
        var logLine = new KustoExportLogLine();
        Assert.AreEqual(" ", logLine.GetSearchableData());
    }

    [TestMethod]
    public void GetMessage_WithEventMessage_ReturnsEventMessage()
    {
        var logLine = new KustoExportLogLine
        {
            Message = "Raw message",
            EventMessage = "Event message"
        };

        Assert.AreEqual("Event message", logLine.GetMessage());
    }

    [TestMethod]
    public void GetMessage_WithoutEventMessage_ReturnsMessage()
    {
        var logLine = new KustoExportLogLine { Message = "Raw message" };
        Assert.AreEqual("Raw message", logLine.GetMessage());
    }

    [TestMethod]
    public void GetMessage_WithEmptyEventMessage_ReturnsMessage()
    {
        var logLine = new KustoExportLogLine
        {
            Message = "Raw message",
            EventMessage = ""
        };

        Assert.AreEqual("Raw message", logLine.GetMessage());
    }

    [TestMethod]
    public void GetMessage_BothEmpty_ReturnsEmpty()
    {
        var logLine = new KustoExportLogLine();
        Assert.AreEqual(string.Empty, logLine.GetMessage());
    }

    [TestMethod]
    public void GetResultSource_ReturnsFileName()
    {
        var logLine = new KustoExportLogLine { FileName = "source.txt" };
        Assert.AreEqual("source.txt", logLine.GetResultSource());
    }

    [TestMethod]
    public void WriteToConsole_DoesNotThrow()
    {
        var logLine = new KustoExportLogLine { Message = "Test" };
        logLine.WriteToConsole(); // Should not throw
        Assert.IsTrue(true);
    }

    [TestMethod]
    public void DefaultValues_AreEmpty()
    {
        var logLine = new KustoExportLogLine();

        Assert.AreEqual(string.Empty, logLine.ActivityId);
        Assert.AreEqual(string.Empty, logLine.Pid);
        Assert.AreEqual(string.Empty, logLine.ProviderName);
        Assert.AreEqual(string.Empty, logLine.TaskName);
        Assert.AreEqual(string.Empty, logLine.Message);
        Assert.AreEqual(string.Empty, logLine.EventMessage);
        Assert.AreEqual(string.Empty, logLine.Level);
        Assert.AreEqual(string.Empty, logLine.HostInstance);
        Assert.AreEqual(string.Empty, logLine.FileName);
    }

    [TestMethod]
    public void Parse_WithEmptyFields_PopulatesEmptyStrings()
    {
        var headers = new[] { "PreciseTimeStamp", "ActivityId", "ProviderName" };
        var line = "2024-01-01 10:00:00\t\tProvider"; // Empty ActivityId

        var logLine = KustoExportLogLine.Parse(line, headers, "test.txt");

        Assert.IsNotNull(logLine);
        Assert.AreEqual(string.Empty, logLine.ActivityId);
        Assert.AreEqual("Provider", logLine.ProviderName);
    }

    [TestMethod]
    public void GetLevel_EmptyLevelString_ReturnsInfo()
    {
        var logLine = new KustoExportLogLine { Level = "" };
        Assert.AreEqual(FindNeedlePluginLib.Level.Info, logLine.GetLevel());
    }
}
