using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FindNeedlePluginLib;
using FindNeedlePluginLib.Interfaces;
using findneedle.Implementations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CoreTests;

/// <summary>
/// A folder load must survive files it cannot read. Regression cover for the crash where opening
/// C:\Windows\System32\winevt\Logs unelevated threw UnauthorizedAccessException out of ProcessFile;
/// because ProcessFile runs in a Task per file and Step2 blocks on those tasks, it surfaced as an
/// AggregateException on the search task, reached an async void UI handler, and tripped a WinUI
/// failfast (0xC000027B) that App.UnhandledException cannot intercept — the app died silently.
/// </summary>
[TestClass]
public class FolderLocationResilienceTests
{
    private string _dir;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fn-folderloc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        ThrowingProcessor.Reset();
    }

    [TestCleanup]
    public void Cleanup()
    {
        ThrowingProcessor.Reset();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>FolderLocation clones a FRESH processor per file (Activator.CreateInstance on the
    /// template's type), so the double is configured through statics, not instance state.</summary>
    private FolderLocation MakeLocation()
    {
        var loc = new FolderLocation { path = _dir };
        loc.SetExtensionProcessorList(new List<IFileExtensionProcessor> { new ThrowingProcessor() });
        return loc;
    }

    [TestMethod]
    public void LoadInMemory_FileThatCannotBeRead_IsSkipped_NotThrown()
    {
        File.WriteAllText(Path.Combine(_dir, "denied.fake"), "x");
        ThrowingProcessor.ToThrow = () => new UnauthorizedAccessException("Access to the path is denied.");

        var loc = MakeLocation();
        loc.LoadInMemory(CancellationToken.None); // must NOT throw

        CollectionAssert.AreEqual(new[] { "denied.fake" }, SkippedNames(loc),
            "the unreadable file should be recorded as skipped");
    }

    [TestMethod]
    public void LoadInMemory_OneBadFile_DoesNotStopTheGoodOnes()
    {
        File.WriteAllText(Path.Combine(_dir, "bad.fake"), "x");
        File.WriteAllText(Path.Combine(_dir, "good.fake"), "x");
        ThrowingProcessor.ToThrow = () => new IOException("The process cannot access the file.");
        ThrowingProcessor.OnlyFailFileNamed = "bad.fake";

        var loc = MakeLocation();
        loc.LoadInMemory(CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "bad.fake" }, SkippedNames(loc));
        CollectionAssert.Contains(ThrowingProcessor.LoadedFiles.ToList(), "good.fake",
            "a sibling file must still be processed after one file fails");
    }

    [TestMethod]
    public void LoadInMemory_Cancellation_IsNotRecordedAsAnUnreadableFile()
    {
        File.WriteAllText(Path.Combine(_dir, "cancel.fake"), "x");
        ThrowingProcessor.ToThrow = () => new OperationCanceledException();

        var loc = MakeLocation();
        try { loc.LoadInMemory(CancellationToken.None); }
        catch (AggregateException) { /* cancellation propagates out of the file tasks — that is fine */ }

        Assert.AreEqual(0, loc.SkippedFiles.Count,
            "a cancellation is a user action, not a file fault");
    }

    [TestMethod]
    public void SkippedFiles_ClearedOnReload()
    {
        File.WriteAllText(Path.Combine(_dir, "denied.fake"), "x");
        ThrowingProcessor.ToThrow = () => new UnauthorizedAccessException("nope");

        var loc = MakeLocation();
        loc.LoadInMemory(CancellationToken.None);
        Assert.AreEqual(1, loc.SkippedFiles.Count);

        loc.LoadInMemory(CancellationToken.None);
        Assert.AreEqual(1, loc.SkippedFiles.Count, "a re-load must not accumulate stale skips");
    }

    private static List<string> SkippedNames(FolderLocation loc)
        => loc.SkippedFiles.Select(x => Path.GetFileName(x.File)).OrderBy(x => x).ToList();

    /// <summary>Processor for ".fake" that throws from OpenFile — standing in for a protected .evtx.</summary>
    private sealed class ThrowingProcessor : IFileExtensionProcessor
    {
        // Static because FolderLocation news up one instance per file.
        public static Func<Exception> ToThrow;
        public static string OnlyFailFileNamed;
        private static readonly List<string> _loaded = new();
        public static IEnumerable<string> LoadedFiles { get { lock (_loaded) return _loaded.ToList(); } }

        public static void Reset()
        {
            ToThrow = null;
            OnlyFailFileNamed = null;
            lock (_loaded) _loaded.Clear();
        }

        private string _file = "";

        public List<string> RegisterForExtensions() => new() { ".fake" };

        public void OpenFile(string fileName)
        {
            _file = fileName;
            if (ToThrow != null && (OnlyFailFileNamed == null || Path.GetFileName(fileName) == OnlyFailFileNamed))
                throw ToThrow();
        }

        public bool CheckFileFormat() => true;
        public void DoPreProcessing() { }
        public void DoPreProcessing(CancellationToken cancellationToken) { }
        public void LoadInMemory() { lock (_loaded) _loaded.Add(Path.GetFileName(_file)); }
        public void LoadInMemory(CancellationToken cancellationToken) => LoadInMemory();
        public List<ISearchResult> GetResults() => new();
        public System.Threading.Tasks.Task GetResultsWithCallback(
            Action<List<ISearchResult>> onBatch, CancellationToken cancellationToken = default, int batchSize = 1000)
            => System.Threading.Tasks.Task.CompletedTask;
        public string GetFileName() => _file;
        public Dictionary<string, int> GetProviderCount() => new();
        public (TimeSpan? timeTaken, int? recordCount) GetSearchPerformanceEstimate(CancellationToken cancellationToken = default) => (null, null);
        public void Dispose() { }
    }
}
