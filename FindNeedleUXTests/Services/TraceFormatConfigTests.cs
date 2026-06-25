using System;
using System.IO;
using FindNeedleUX.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FindNeedleUXTests.Services;

/// <summary>
/// Tests for <see cref="TraceFormatConfig.Apply"/> — it exports the WPP symbol settings into the
/// TRACE_FORMAT_SEARCH_PATH / _NT_SYMBOL_PATH environment variables the trace tools read.
/// </summary>
[TestClass]
[TestCategory("Services")]
[DoNotParallelize] // mutates process environment variables + the ResultsViewerSettings singleton
public class TraceFormatConfigTests
{
    private string _origTmf, _origSym, _settingsFile;

    [TestInitialize]
    public void Init()
    {
        _origTmf = Environment.GetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH");
        _origSym = Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH");
        _settingsFile = Path.Combine(Path.GetTempPath(), $"viewer-settings-tfc-{Guid.NewGuid():N}.json");
        ResultsViewerSettings.SetStorageLocationForTests(_settingsFile);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH", _origTmf);
        Environment.SetEnvironmentVariable("_NT_SYMBOL_PATH", _origSym);
        try { if (File.Exists(_settingsFile)) File.Delete(_settingsFile); } catch { }
        ResultsViewerSettings.ResetStorageForTests();
    }

    [TestMethod]
    public void Apply_ExportsConfiguredPathsToEnvironment()
    {
        ResultsViewerSettings.TraceFormatSearchPath = @"C:\my\tmf";
        ResultsViewerSettings.SymbolPath = @"C:\my\symbols";

        TraceFormatConfig.Apply();

        StringAssert.Contains(Environment.GetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH") ?? "", @"C:\my\tmf");
        StringAssert.Contains(Environment.GetEnvironmentVariable("_NT_SYMBOL_PATH") ?? "", @"C:\my\symbols");
    }

    [TestMethod]
    public void Apply_EmptySymbolPath_DoesNotThrow_AndSetsTmf()
    {
        ResultsViewerSettings.TraceFormatSearchPath = @"D:\tmfs";
        ResultsViewerSettings.SymbolPath = "";

        TraceFormatConfig.Apply(); // must not throw

        StringAssert.Contains(Environment.GetEnvironmentVariable("TRACE_FORMAT_SEARCH_PATH") ?? "", @"D:\tmfs");
    }
}
