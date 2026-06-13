using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using FindNeedlePluginLib;
using FindNeedleRuleDSL;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleRuleDSLTests;

/// <summary>
/// Direct tests for <see cref="OutputRuleProcessor"/>. Covers TESTING_PLAN.md
/// R-05..R-08, R-10: CSV escaping, JSON/XML validity, XML name sanitisation,
/// path placeholder expansion, unknown format graceful skip.
///
/// The class was ~788 lines with zero direct tests before this file landed —
/// previously exercised only incidentally via integration suites.
/// </summary>
[TestClass]
[TestCategory("OutputProcessor")]
public class OutputRuleProcessorTests
{
    private string _scratchDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _scratchDir = Path.Combine(Path.GetTempPath(), $"OutputRuleProcessorTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_scratchDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_scratchDir)) Directory.Delete(_scratchDir, recursive: true); } catch { }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a single-rule output section as nested dictionaries — matches
    /// the IDictionary&lt;string,object?&gt; branch of GetRulesFromSection.
    /// </summary>
    private static IDictionary<string, object?> BuildOutputSection(
        string format,
        string path,
        IEnumerable<string>? fields = null,
        bool includeHeaders = true,
        string? delimiter = null,
        bool pretty = true,
        string? match = null,
        bool enabled = true)
    {
        var action = new Dictionary<string, object?>
        {
            ["type"] = "output",
            ["format"] = format,
            ["path"] = path,
            ["includeHeaders"] = includeHeaders,
            ["pretty"] = pretty,
        };
        if (fields != null) action["fields"] = fields.Cast<object>().ToList();
        if (delimiter != null) action["delimiter"] = delimiter;

        var rule = new Dictionary<string, object?>
        {
            ["name"] = "TestRule",
            ["enabled"] = enabled,
            ["action"] = action,
        };
        if (match != null) rule["match"] = match;

        return new Dictionary<string, object?>
        {
            ["name"] = "TestSection",
            ["rules"] = new List<object> { rule },
        };
    }

    private static List<ISearchResult> Results(params StubResult[] items) =>
        items.Cast<ISearchResult>().ToList();

    /// <summary>
    /// Full ISearchResult fake — FindNeedlePluginLib.TestClasses.FakeSearchResult
    /// throws NotImplementedException on five getters that OutputRuleProcessor
    /// needs (Level, Source, MachineName, etc.), so we define a complete stub here.
    /// </summary>
    private sealed class StubResult : ISearchResult
    {
        public DateTime LogTime { get; init; } = new DateTime(2026, 1, 2, 3, 4, 5);
        public string Message { get; init; } = "msg";
        public string SearchableData { get; init; } = "msg";
        public string Source { get; init; } = "src";
        public string MachineName { get; init; } = "host";
        public string Username { get; init; } = "user";
        public string TaskName { get; init; } = "task";
        public string OpCode { get; init; } = "op";
        public string ResultSource { get; init; } = "file.log";
        public Level Level { get; init; } = Level.Info;

        public DateTime GetLogTime() => LogTime;
        public string GetMessage() => Message;
        public string GetSearchableData() => SearchableData;
        public string GetSource() => Source;
        public string GetMachineName() => MachineName;
        public string GetUsername() => Username;
        public string GetTaskName() => TaskName;
        public string GetOpCode() => OpCode;
        public string GetResultSource() => ResultSource;
        public Level GetLevel() => Level;
        public void WriteToConsole() { }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R-05: CSV escaping for comma, quote, newline, CR.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void R05_Csv_EscapesCommaQuoteAndNewline()
    {
        var outPath = Path.Combine(_scratchDir, "escape.csv");
        var section = BuildOutputSection("csv", outPath, fields: new[] { "message" });
        var results = Results(
            new StubResult { Message = "value, with comma" },
            new StubResult { Message = "value with \"quote\"" },
            new StubResult { Message = "value\nwith\nnewlines" },
            new StubResult { Message = "value\rwith\rCR" }
        );

        new OutputRuleProcessor().ProcessOutputRules(results, new object[] { section });

        Assert.IsTrue(File.Exists(outPath), "Output file should be written.");
        var text = File.ReadAllText(outPath);

        // Each value containing a special character must be wrapped in double-quotes;
        // embedded quotes must be doubled.
        StringAssert.Contains(text, "\"value, with comma\"");
        StringAssert.Contains(text, "\"value with \"\"quote\"\"\"");
        StringAssert.Contains(text, "\"value\nwith\nnewlines\"");
        StringAssert.Contains(text, "\"value\rwith\rCR\"");
    }

    [TestMethod]
    public void R05_Csv_HonorsCustomDelimiter()
    {
        var outPath = Path.Combine(_scratchDir, "tsv.csv");
        var section = BuildOutputSection("csv", outPath,
            fields: new[] { "message", "source" },
            delimiter: "\t");
        var results = Results(new StubResult { Message = "m", Source = "s" });

        new OutputRuleProcessor().ProcessOutputRules(results, new object[] { section });

        var lines = File.ReadAllLines(outPath);
        // Header + 1 data row.
        Assert.AreEqual(2, lines.Length);
        Assert.AreEqual("message\tsource", lines[0]);
        Assert.AreEqual("m\ts", lines[1]);
    }

    [TestMethod]
    public void R05_Csv_RespectsIncludeHeadersFalse()
    {
        var outPath = Path.Combine(_scratchDir, "noheaders.csv");
        var section = BuildOutputSection("csv", outPath,
            fields: new[] { "message" }, includeHeaders: false);
        var results = Results(new StubResult { Message = "only-row" });

        new OutputRuleProcessor().ProcessOutputRules(results, new object[] { section });

        var lines = File.ReadAllLines(outPath);
        Assert.AreEqual(1, lines.Length);
        Assert.AreEqual("only-row", lines[0]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R-06: JSON output is parseable for 0, 1, and many results.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void R06_Json_EmptyResults_ProducesParseableArray()
    {
        var outPath = Path.Combine(_scratchDir, "empty.json");
        var section = BuildOutputSection("json", outPath, fields: new[] { "message" });

        new OutputRuleProcessor().ProcessOutputRules(new List<ISearchResult>(), new object[] { section });

        Assert.IsTrue(File.Exists(outPath));
        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        Assert.AreEqual(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.AreEqual(0, doc.RootElement.GetArrayLength());
    }

    [TestMethod]
    public void R06_Json_SingleResult_ContainsSelectedFields()
    {
        var outPath = Path.Combine(_scratchDir, "one.json");
        var section = BuildOutputSection("json", outPath, fields: new[] { "message", "source" });
        var results = Results(new StubResult { Message = "hello", Source = "log.txt" });

        new OutputRuleProcessor().ProcessOutputRules(results, new object[] { section });

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        Assert.AreEqual(1, doc.RootElement.GetArrayLength());
        var row = doc.RootElement[0];
        Assert.AreEqual("hello", row.GetProperty("message").GetString());
        Assert.AreEqual("log.txt", row.GetProperty("source").GetString());
    }

    [TestMethod]
    public void R06_Json_ManyResults_AllPresentAndParseable()
    {
        var outPath = Path.Combine(_scratchDir, "many.json");
        var section = BuildOutputSection("json", outPath, fields: new[] { "message" });
        var results = Enumerable.Range(0, 5_000)
            .Select(i => (ISearchResult)new StubResult { Message = $"m{i}" })
            .ToList();

        new OutputRuleProcessor().ProcessOutputRules(results, new object[] { section });

        using var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        Assert.AreEqual(5_000, doc.RootElement.GetArrayLength());
        Assert.AreEqual("m0", doc.RootElement[0].GetProperty("message").GetString());
        Assert.AreEqual("m4999", doc.RootElement[4_999].GetProperty("message").GetString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R-07: XML output is well-formed; sanitised element names.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void R07_Xml_ProducesWellFormedDocument()
    {
        var outPath = Path.Combine(_scratchDir, "out.xml");
        var section = BuildOutputSection("xml", outPath, fields: new[] { "message" });
        var results = Results(
            new StubResult { Message = "first" },
            new StubResult { Message = "with <angle> & ampersand" }
        );

        new OutputRuleProcessor().ProcessOutputRules(results, new object[] { section });

        // Should parse without throwing.
        var doc = XDocument.Load(outPath);
        Assert.AreEqual("Results", doc.Root!.Name.LocalName);
        var rows = doc.Root.Elements("Result").ToList();
        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("with <angle> & ampersand", rows[1].Element("message")!.Value);
    }

    [TestMethod]
    public void R07_Xml_SanitisesElementNamesWithSpecialChars()
    {
        // Field name with special chars + digit prefix. The writer falls through
        // GetFieldValue's default branch (unknown field → GetSearchableData) but
        // the *element name* must still be sanitised so the XML is valid.
        var outPath = Path.Combine(_scratchDir, "sanitise.xml");
        var section = BuildOutputSection("xml", outPath,
            fields: new[] { "1-weird name!" });
        var results = Results(new StubResult { SearchableData = "ok" });

        new OutputRuleProcessor().ProcessOutputRules(results, new object[] { section });

        // If element name wasn't sanitised, XDocument.Load would throw on a digit-prefixed name.
        var doc = XDocument.Load(outPath);
        var element = doc.Root!.Element("Result")!.Elements().Single();
        // Sanitiser replaces non-alnum/underscore with _, and underscore-prefixes digit-starts.
        Assert.AreEqual("_1_weird_name_", element.Name.LocalName);
        Assert.AreEqual("ok", element.Value);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R-08: Path placeholder expansion ({date}, {time}, {datetime}, {output}).
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void R08_PathPlaceholders_DateAndTimeExpandToCorrectFormats()
    {
        // Use {output} so we can find the actual file under the app's output folder
        // — the processor mints a real timestamp at write time, so we match by pattern.
        var section = BuildOutputSection(
            "csv",
            Path.Combine(_scratchDir, "stamp-{date}-{time}.csv"),
            fields: new[] { "message" },
            includeHeaders: false);
        var results = Results(new StubResult { Message = "x" });

        new OutputRuleProcessor().ProcessOutputRules(results, new object[] { section });

        // {date} = yyyy-MM-dd, {time} = HHmmss
        var files = Directory.GetFiles(_scratchDir, "stamp-*.csv");
        Assert.AreEqual(1, files.Length, "Exactly one timestamped file should be written.");
        var name = Path.GetFileName(files[0]);
        StringAssert.Matches(name, new System.Text.RegularExpressions.Regex(@"^stamp-\d{4}-\d{2}-\d{2}-\d{6}\.csv$"),
            $"Filename '{name}' should match stamp-yyyy-MM-dd-HHmmss.csv");
    }

    [TestMethod]
    public void R08_PathPlaceholders_DatetimeExpandsToSingleToken()
    {
        var section = BuildOutputSection(
            "csv",
            Path.Combine(_scratchDir, "combined-{datetime}.csv"),
            fields: new[] { "message" },
            includeHeaders: false);
        new OutputRuleProcessor().ProcessOutputRules(
            Results(new StubResult()), new object[] { section });

        var files = Directory.GetFiles(_scratchDir, "combined-*.csv");
        Assert.AreEqual(1, files.Length);
        StringAssert.Matches(Path.GetFileName(files[0]),
            new System.Text.RegularExpressions.Regex(@"^combined-\d{4}-\d{2}-\d{2}_\d{6}\.csv$"));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // R-10: Unknown format does not crash, does not write a file.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void R10_UnknownFormat_DoesNotThrow_AndProducesNoFile()
    {
        var outPath = Path.Combine(_scratchDir, "out.weirdformat");
        var section = BuildOutputSection("weirdformat", outPath, fields: new[] { "message" });

        // Must not throw — current code logs to Debug and skips the unknown switch case.
        new OutputRuleProcessor().ProcessOutputRules(
            Results(new StubResult()), new object[] { section });

        Assert.IsFalse(File.Exists(outPath),
            "Unknown format should fall through the switch without writing a file.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bonus: disabled rule is skipped entirely.
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public void DisabledRule_ProducesNoOutput()
    {
        var outPath = Path.Combine(_scratchDir, "should-not-exist.csv");
        var section = BuildOutputSection("csv", outPath, enabled: false);

        new OutputRuleProcessor().ProcessOutputRules(
            Results(new StubResult()), new object[] { section });

        Assert.IsFalse(File.Exists(outPath));
    }

    [TestMethod]
    public void Txt_OutputContainsPipeDelimitedFields()
    {
        var outPath = Path.Combine(_scratchDir, "out.txt");
        var section = BuildOutputSection("txt", outPath,
            fields: new[] { "message", "source" }, includeHeaders: false);
        var results = Results(new StubResult { Message = "m", Source = "s" });

        new OutputRuleProcessor().ProcessOutputRules(results, new object[] { section });

        var text = File.ReadAllText(outPath).TrimEnd('\r', '\n');
        Assert.AreEqual("message=m | source=s", text);
    }
}
